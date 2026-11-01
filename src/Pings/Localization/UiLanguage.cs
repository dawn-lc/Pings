using System.Globalization;

namespace Pings.Localization
{
    /// <summary>
    /// 界面语言的选定与切换。
    /// </summary>
    /// <remarks>
    /// 语言靠 <see cref="CultureInfo.CurrentUICulture" /> 表达，而不是自己维护一个「当前实现」
    /// 字段：这样 <c>Resources</c> 的每个属性只要按当前 UI 文化查表即可，
    /// 与 .NET 的既有约定一致，也便于测试里临时切换。
    ///
    /// 只改 <see cref="CultureInfo.CurrentUICulture" />，**不改** <see cref="CultureInfo.CurrentCulture" />：
    /// 数字与日期的格式化在本项目里一律用显式格式串或 <see cref="CultureInfo.InvariantCulture" />，
    /// 让它们继续跟随系统文化，避免「切界面语言把日志时间格式也改了」这种副作用。
    /// </remarks>
    public static class UiLanguage
    {
        /// <summary>表示「跟随系统」的标记。</summary>
        public const string Auto = "auto";

        /// <summary>
        /// 界面支持的语言（不含 <see cref="Auto" />）。**这是唯一真相源**：
        /// <see cref="TryResolve" />、<see cref="SupportedMarkers" /> 与配置校验提示都从这里派生。
        /// </summary>
        /// <remarks>
        /// 为什么必须显式列出而不能从 resx 文件名推导：文件名后缀（<c>ZhCn</c>）与 culture
        /// 标记（<c>zh-CN</c>）之间没有机器可读的映射（见 csproj 的 LogicalName 注释）。
        ///
        /// 新增一种语言只需三处：① 本数组加一项；② <c>Properties\ResourcesXx.resx</c>；
        /// ③ csproj 里对应的 <c>LogicalName</c>。<c>LocalizationTests</c> 会校验本数组里的
        /// 每一项都真能解析到资源，漏写会产生「帮助里写了却选不中」这类不一致。
        /// </remarks>
        public static readonly string[] SupportedCultures = ["en", "zh-CN"];

        /// <summary>受支持的全部语言标记（<see cref="Auto" /> + 具体语言），用于帮助文本与校验提示。</summary>
        /// <remarks>
        /// 写成属性而不是字段：字段初始化按声明顺序执行，把它放在 <see cref="SupportedCultures" />
        /// 之前会展开一个还没赋值的 <see langword="null" />。属性按需求值，没有这个耦合。
        /// </remarks>
        public static IReadOnlyList<string> SupportedMarkers => [Auto, .. SupportedCultures];

        /// <summary>可用于提示文案的「auto / en / zh-CN」。</summary>
        public static string SupportedMarkerList => string.Join(" / ", SupportedMarkers);

        /// <summary>
        /// 解析语言标记。<c>auto</c> / 空值成功且 <paramref name="culture" /> 为
        /// <see langword="null" />（表示「不改」）；未知标记返回 <see langword="false" />。
        /// </summary>
        /// <remarks>
        /// 匹配规则全部派生自 <see cref="SupportedCultures" />：
        /// 先精确匹配，再按主语言标签（第一段）匹配。因此 <c>zh</c> / <c>zh-Hans</c> /
        /// <c>zh-TW</c> / <c>zh-Hant-HK</c> 都落到 <c>zh-CN</c>，<c>en-US</c> 落到 <c>en</c>。
        ///
        /// 先精确匹配是为了将来同时支持 <c>zh-CN</c> 与 <c>zh-TW</c> 时，
        /// <c>zh-TW</c> 不会被排在数组前面的 <c>zh-CN</c> 按主标签抢走。
        ///
        /// 这里只决定「编辑哪个 culture」；具体回退到哪份资源由
        /// <see cref="Properties.EmbeddedResourceManager" /> 的文化链负责，两处不重复。
        /// </remarks>
        public static bool TryResolve(string? marker, out CultureInfo? culture)
        {
            culture = null;

            if (string.IsNullOrWhiteSpace(marker))
                return true;

            // 下划线写法（zh_CN / en_US）不是合法 BCP-47，但用户很可能这么写，顺手归一。
            string value = marker.Trim().Replace('_', '-');

            if (value.Equals(Auto, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (string supported in SupportedCultures)
            {
                if (value.Equals(supported, StringComparison.OrdinalIgnoreCase))
                {
                    culture = new CultureInfo(supported);
                    return true;
                }
            }

            string primary = PrimarySubtag(value);

            foreach (string supported in SupportedCultures)
            {
                if (primary.Equals(PrimarySubtag(supported), StringComparison.OrdinalIgnoreCase))
                {
                    culture = new CultureInfo(supported);
                    return true;
                }
            }

            return false;
        }

        /// <summary>取语言标签的第一段（<c>zh-Hant-HK</c> → <c>zh</c>）。</summary>
        private static string PrimarySubtag(string tag)
        {
            int dash = tag.IndexOf('-');
            return dash < 0 ? tag : tag[..dash];
        }

        /// <summary>
        /// 应用语言标记。<c>auto</c> / 未知标记一律**不动**当前语言。
        /// </summary>
        /// <remarks>
        /// <c>auto</c> 的语义是「跟随系统」，而系统语言就是进程当前的
        /// <see cref="CultureInfo.CurrentUICulture" />——什么都不做就是最准确的实现。
        ///
        /// 刻意**不**缓存一份「启动时的文化」用于还原：那份静态快照会让行为依赖
        /// 「类型什么时候第一次被碰」——谁先改过 UI 文化，auto 就会把谁改回去。
        /// 测试里多个用例共享进程，这个隐式耦合会变成难以定位的失败。
        ///
        /// 未知标记同样不报错也不改动：报错由 <c>CliOptions</c>（命令行）与
        /// <c>ConfigValidator</c>（配置文件）负责，它们才有能力输出提示并拒绝启动。
        /// </remarks>
        public static void Apply(string? marker)
        {
            if (TryResolve(marker, out CultureInfo? culture) && culture is not null)
                CultureInfo.CurrentUICulture = culture;
        }
    }
}
