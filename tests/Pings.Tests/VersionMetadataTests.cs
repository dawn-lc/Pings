using System.Reflection;
using System.Text.RegularExpressions;
using Pings.Diagnostics;

namespace Pings.Tests;

/// <summary>
/// 守护「版本号 = git tag」这条链路。
/// </summary>
/// <remarks>
/// 版本号不正确是典型的静默失效：程序照常运行，只有用户看到的版本是错的。
/// 这里断言的是 csproj → AssemblyMetadata → AppInfo 的完整通路，
/// 而不是某个常量。
/// </remarks>
public partial class VersionMetadataTests
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex SemanticVersion();

    [Fact]
    public void AppVersion元数据已注入且为三段版本号()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.Matches(SemanticVersion(), AppInfo.Version);
    }

    [Fact]
    public void AppVersionType元数据已注入()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.VersionType));
    }

    [Fact]
    public void 元数据能被程序集特性读到()
    {
        // 直接查程序集，确认不是靠 AppInfo 内部的兜底值凑出来的。
        AssemblyMetadataAttribute[] metadata = typeof(AppInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToArray();

        Assert.Contains(metadata, attribute => attribute.Key == "AppVersion");
        Assert.Contains(metadata, attribute => attribute.Key == "AppVersionType");
    }

    [Fact]
    public void 本地构建标记为开发类型()
    {
        // 测试跑的是本地构建；若这里失败，说明有人把 Dev 默认值删了，
        // 发布包也就无法与开发包区分。
        Assert.True(AppInfo.IsDevelopment);
        Assert.False(AppInfo.IsRelease);
    }

    [Theory]
    [InlineData("1.0.22", "Release", "Pings 1.0.22")]
    [InlineData("1.0.22", "release", "Pings 1.0.22")]
    [InlineData("1.1.0", "Beta", "Pings 1.1.0-Beta")]
    [InlineData("0.0.0", "Dev", "Pings 0.0.0-Dev")]
    public void 版本文本在非正式渠道带类型后缀(string version, string type, string expected)
    {
        Assert.Equal(expected, AppInfo.FormatVersionText(version, type));
    }

    [Fact]
    public void 版本文本与实际元数据一致()
    {
        Assert.Equal(AppInfo.FormatVersionText(AppInfo.Version, AppInfo.VersionType), AppInfo.VersionText);
    }

    [Fact]
    public void 正式渠道的版本文本与工作流断言规则一致()
    {
        // Release.yml 用同样的规则拼期望值（Release 不带后缀），
        // 两边规则不一致会导致发布步骤莫名失败。
        Assert.Equal($"Pings {AppInfo.Version}", AppInfo.FormatVersionText(AppInfo.Version, AppInfo.ReleaseType));
    }
}
