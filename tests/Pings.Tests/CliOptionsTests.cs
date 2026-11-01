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
    public void Parse_双横线之后按位置参数处理()
    {
        CliOptions options = CliOptions.Parse(["--", "--not-an-option.json"], out string? error);

        Assert.Null(error);
        Assert.Equal("--not-an-option.json", options.ConfigPath);
    }

    [Fact]
    public void 每个退出码都有中文说明()
    {
        int[] codes =
        [
            ExitCodes.Success,
            ExitCodes.InvalidArguments,
            ExitCodes.Configuration,
            ExitCodes.RuntimeError,
            ExitCodes.Unreachable
        ];

        Assert.All(codes, code => Assert.NotEqual("未知退出码", ExitCodes.Describe(code)));
    }
}
