using Pings.Cli;

namespace Pings.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_无参数_返回默认选项()
    {
        CliOptions options = CliOptions.Parse([], out string? error);

        Assert.Null(error);
        Assert.Null(options.ConfigPath);
        Assert.False(options.Once);
        Assert.False(options.ShowHelp);
        Assert.False(options.ShowVersion);
    }

    [Fact]
    public void Parse_位置参数作为配置路径()
    {
        CliOptions options = CliOptions.Parse(["my-config.json"], out string? error);

        Assert.Null(error);
        Assert.Equal("my-config.json", options.ConfigPath);
    }

    [Fact]
    public void Parse_命名参数与开关()
    {
        CliOptions options = CliOptions.Parse(
            ["--config", "a.json", "--once", "--no-color", "--verbose"],
            out string? error);

        Assert.Null(error);
        Assert.Equal("a.json", options.ConfigPath);
        Assert.True(options.Once);
        Assert.True(options.NoColor);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Parse_缺少配置路径_报错()
    {
        CliOptions options = CliOptions.Parse(["--config"], out string? error);

        Assert.NotNull(error);
        Assert.Null(options.ConfigPath);
    }

    [Fact]
    public void Parse_未知参数_报错()
    {
        CliOptions options = CliOptions.Parse(["--nope"], out string? error);

        Assert.NotNull(error);
        Assert.Contains("--nope", error);
        Assert.Null(options.ConfigPath);
    }

    [Fact]
    public void Parse_重复指定配置文件_报错()
    {
        CliOptions options = CliOptions.Parse(["a.json", "b.json"], out string? error);

        Assert.NotNull(error);
        Assert.Null(options.ConfigPath);
    }

    [Fact]
    public void Parse_指定受支持的语言()
    {
        foreach (string language in new[] { "auto", "en", "zh-CN", "zh", "en-US" })
        {
            CliOptions options = CliOptions.Parse(["--lang", language], out string? error);

            Assert.Null(error);
            Assert.Equal(language, options.Language);
        }
    }

    [Fact]
    public void Parse_未知语言_报错()
    {
        CliOptions options = CliOptions.Parse(["--lang", "fr"], out string? error);

        Assert.NotNull(error);
        Assert.Contains("fr", error);
        Assert.Null(options.Language);
    }

    [Fact]
    public void Parse_双横线之后按位置参数处理()
    {
        CliOptions options = CliOptions.Parse(["--", "--not-an-option.json"], out string? error);

        Assert.Null(error);
        Assert.Equal("--not-an-option.json", options.ConfigPath);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Once隐含不等待输入(bool once, bool noPause, bool expected)
    {
        // --once 定位是「适合脚本/CI」：即使没配 --no-pause 也不能在末尾
        // 或「是否创建默认配置」上停住等按键，否则脚本里一次笔误就变成挂起。
        string[] args = [.. once ? new[] { "--once" } : [], .. noPause ? new[] { "--no-pause" } : []];

        CliOptions options = CliOptions.Parse(args, out string? error);

        Assert.Null(error);
        Assert.Equal(expected, options.IsNonInteractive);
    }

    [Fact]
    public void 每个退出码都有说明()
    {
        int[] codes =
        [
            ExitCodes.Success,
            ExitCodes.InvalidArguments,
            ExitCodes.Configuration,
            ExitCodes.RuntimeError,
            ExitCodes.Unreachable
        ];

        // 断言与具体语言无关：未知码回退到固定文案，已知码都不能落到它。
        string unknown = ExitCodes.Describe(int.MinValue);
        Assert.All(codes, code => Assert.NotEqual(unknown, ExitCodes.Describe(code)));
    }
}
