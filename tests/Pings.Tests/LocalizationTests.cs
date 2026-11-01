using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pings.Localization;
using Pings.Monitoring;
using Pings.Properties;

namespace Pings.Tests;

/// <summary>
/// 多语言资源的自洽性检查。
/// </summary>
/// <remarks>
/// 这些断言的目的不是「测试代码」，而是把「资源文件悄悄坏掉」变成构建期可见的失败：
/// 少一个键、少一个占位符、某个文化没被解析到，都会在 CI 上直接红。
/// 手工维护 resx 与 Resources.Designer.cs 时尤其重要——编译器管不到它们。
/// </remarks>
public class LocalizationTests
{
    private const string NeutralResourcePath = "src/Pings/Properties/Resources.resx";
    private const string ChineseResourcePath = "src/Pings/Properties/ResourcesZhCn.resx";

    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>从仓库根目录向上找到仓库根（测试运行时的工作目录是 bin 目录）。</summary>
    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pings.sln")))
                directory = directory.Parent;

            Assert.NotNull(directory);
            return directory!.FullName;
        }
    }

    private static Dictionary<string, string> Load(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot, relativePath);
        Assert.True(File.Exists(path), $"资源文件不存在: {path}");

        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    [Fact]
    public void 两种语言的键完全一致()
    {
        HashSet<string> neutral = [.. Load(NeutralResourcePath).Keys];
        HashSet<string> chinese = [.. Load(ChineseResourcePath).Keys];

        Assert.Empty(neutral.Except(chinese).Order());
        Assert.Empty(chinese.Except(neutral).Order());
    }

    [Fact]
    public void 同一键的占位符集合一致()
    {
        Dictionary<string, string> neutral = Load(NeutralResourcePath);
        Dictionary<string, string> chinese = Load(ChineseResourcePath);

        foreach ((string key, string value) in neutral)
        {
            string[] expected = [.. PlaceholderPattern.Matches(value).Select(match => match.Value).Order()];
            string[] actual = [.. PlaceholderPattern.Matches(chinese[key]).Select(match => match.Value).Order()];

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void 没有空值()
    {
        foreach ((string key, string value) in Load(NeutralResourcePath))
            Assert.False(string.IsNullOrWhiteSpace(value), $"中性资源 {key} 为空");

        foreach ((string key, string value) in Load(ChineseResourcePath))
            Assert.False(string.IsNullOrWhiteSpace(value), $"中文资源 {key} 为空");
    }

    [Fact]
    public void 设计器属性覆盖所有键()
    {
        HashSet<string> keys = [.. Load(NeutralResourcePath).Keys];

        PropertyInfo[] properties = typeof(Resources)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .ToArray();

        HashSet<string> covered = [.. properties.Select(property => property.Name)];

        Assert.Empty(keys.Except(covered).Order());   // 新增 resx 键必须补 Designer 属性
        Assert.Empty(covered.Except(keys).Order());   // 反过来也不能有指向不存在键的属性
    }

    [Fact]
    public void 设计器属性都能取到值()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string culture in new[] { "en", "zh-CN" })
            {
                CultureInfo.CurrentUICulture = new CultureInfo(culture);

                foreach (PropertyInfo property in typeof(Resources).GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    string? value = property.GetValue(null) as string;
                    Assert.False(string.IsNullOrWhiteSpace(value), $"{culture} 下 {property.Name} 取不到值");
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("zh-CN", "通讯正常")]
    [InlineData("zh", "通讯正常")]
    [InlineData("zh-Hans", "通讯正常")]
    [InlineData("zh-Hans-CN", "通讯正常")]
    [InlineData("zh-CHS", "通讯正常")]
    [InlineData("zh-SG", "通讯正常")]
    [InlineData("zh-TW", "通讯正常")]
    [InlineData("zh-Hant", "通讯正常")]
    [InlineData("zh-HK", "通讯正常")]
    [InlineData("en", "OK")]
    [InlineData("en-US", "OK")]
    public void 文化别名都解析到预期文案(string culture, string expected)
    {
        Assert.Equal(expected, EmbeddedResourceManager.GetString(nameof(Resources.StatusSuccess), new CultureInfo(culture)));
    }

    [Fact]
    public void 未支持的文化回退到中性资源()
    {
        // 德语没有译文，必须回退到英文（中性）而不是空白。
        Assert.Equal("OK", EmbeddedResourceManager.GetString(nameof(Resources.StatusSuccess), new CultureInfo("de-DE")));
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("zh-CN", true)]
    [InlineData("zh", true)]
    [InlineData("zh-Hans", true)]
    [InlineData("zh-TW", true)]
    [InlineData("en", true)]
    [InlineData("en-US", true)]
    [InlineData("EN", true)]
    [InlineData("zh_CN", true)]
    [InlineData("fr", false)]
    [InlineData("klingon", false)]
    public void 语言标记解析符合预期(string? marker, bool expected)
    {
        Assert.Equal(expected, UiLanguage.TryResolve(marker, out _));
    }

    [Fact]
    public void 受支持语言清单与实际解析一致()
    {
        // SupportedCultures 是唯一真相源（TryResolve 与帮助文案都从它派生）。
        // 这里反过来校验：清单里的每一项都真能解析、且真能取到资源。
        // 漏写 csproj 的 LogicalName 或 resx 时，这里会直接红，而不是运行时静默退回英文。
        Assert.NotEmpty(UiLanguage.SupportedCultures);

        foreach (string culture in UiLanguage.SupportedCultures)
        {
            Assert.True(UiLanguage.TryResolve(culture, out CultureInfo? resolved), $"{culture} 无法解析");
            Assert.Equal(culture, resolved!.Name);
            Assert.False(string.IsNullOrWhiteSpace(EmbeddedResourceManager.GetString(nameof(Resources.StatusSuccess), resolved)));
        }

        // 帮助与校验提示的清单必须包含 auto，且以它开头。
        Assert.Equal(UiLanguage.Auto, UiLanguage.SupportedMarkers[0]);
        Assert.Equal(UiLanguage.SupportedCultures.Length + 1, UiLanguage.SupportedMarkers.Count);
        Assert.Equal(string.Join(" / ", UiLanguage.SupportedMarkers), UiLanguage.SupportedMarkerList);
    }

    [Fact]
    public void 语言别名都归到清单里的某一项()
    {
        // 主语言标签匹配：任何 zh-* / en-* 变体都必须落到 SupportedCultures 里的同一项，
        // 不允许「解析成功但 culture 不在清单里」。
        string[] aliases =
        [
            "zh", "zh-Hans", "zh-Hans-CN", "zh-CHS", "zh-SG",
            "zh-TW", "zh-Hant", "zh-Hant-TW", "zh-Hant-HK", "zh-HK", "zh-CHT",
            "en-US", "en-GB", "EN-us"
        ];

        foreach (string alias in aliases)
        {
            Assert.True(UiLanguage.TryResolve(alias, out CultureInfo? resolved), $"{alias} 应能解析");
            Assert.Contains(resolved!.Name, UiLanguage.SupportedCultures);
        }
    }

    [Fact]
    public void 精确匹配优先于主语言标签()
    {
        // 目前只有 en / zh-CN，zh-TW 走主标签落到 zh-CN 是正确的；
        // 将来若补上 zh-TW 译文，它必须精确命中自己的那一项而不是被 zh-CN 抢走。
        Assert.True(UiLanguage.TryResolve("zh-TW", out CultureInfo? resolved));

        string expected = UiLanguage.SupportedCultures.Contains("zh-TW") ? "zh-TW" : "zh-CN";
        Assert.Equal(expected, resolved!.Name);
    }

    [Fact]
    public void 测试默认语言固定为英文()
    {
        // 与 CI（英文 runner）保持一致：默认必须是英文，中文断言必须显式开 CultureScope。
        // 这条用例同时守护 TestCultureDefaults 的初始化器——被删掉时它会立刻红。
        Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("OK", ProbeStatus.Success.ToDisplayString());
    }

    [Fact]
    public void 自动语言与未知标记都不改动当前语言()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");

            // auto = 「跟随系统」，即不动当前文化（而不是恢复到某个静态快照）。
            UiLanguage.Apply(UiLanguage.Auto);
            Assert.Equal("zh-CN", CultureInfo.CurrentUICulture.Name);

            UiLanguage.Apply("en");
            Assert.Equal("en", CultureInfo.CurrentUICulture.Name);

            // 未知标记不报错也不改语言（报错交给 CliOptions / ConfigValidator）。
            UiLanguage.Apply("fr");
            Assert.Equal("en", CultureInfo.CurrentUICulture.Name);

            UiLanguage.Apply(null);
            Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void 状态列文案不含空格()
    {
        // 实时表格靠 Overflow.Ellipsis 截断，而折行只发生在空格处：
        // 一旦状态文案里有空格，窄窗口下行高被撑开，整个活动区域布局错乱。
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string culture in new[] { "en", "zh-CN" })
            {
                CultureInfo.CurrentUICulture = new CultureInfo(culture);

                foreach (string key in new[] { "StatusUnknown", "StatusSuccess", "StatusTimeout", "StatusNoReply", "StatusUnreachable", "StatusPortClosed", "StatusDnsFailure", "StatusTlsFailure", "StatusHttpStatusUnexpected", "StatusResponseTimeout", "StatusProtocolError", "StatusLocalError", "StatusFallback" })
                {
                    string value = (string)typeof(Resources).GetProperty(key)!.GetValue(null)!;
                    Assert.DoesNotContain(' ', value);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void 英文文案不应过长()
    {
        // 状态列宽度有限，英文比中文宽：超过 12 字符在窄窗口下会被截成「...」，
        // 用户反而看不出状态。这里给出硬上限，防止后续随手加长词。
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            foreach (string key in new[] { "StatusUnknown", "StatusSuccess", "StatusTimeout", "StatusNoReply", "StatusUnreachable", "StatusPortClosed", "StatusDnsFailure", "StatusTlsFailure", "StatusHttpStatusUnexpected", "StatusResponseTimeout", "StatusProtocolError", "StatusLocalError", "StatusFallback" })
            {
                string value = (string)typeof(Resources).GetProperty(key)!.GetValue(null)!;
                Assert.True(value.Length <= 13, $"{key} 过长: {value}");
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
