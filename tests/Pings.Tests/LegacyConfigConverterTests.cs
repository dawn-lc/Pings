using Pings.Configuration;

namespace Pings.Tests;

public sealed class LegacyConfigConverterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "pings-legacy-tests-" + Guid.NewGuid().ToString("N"));

    public LegacyConfigConverterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void 解析完整配置行()
    {
        string path = Path.Combine(_directory, "config.txt");
        File.WriteAllText(path, "gateway 192.168.1.1 2000 150 25 60 8");

        AppConfig config = LegacyConfigConverter.Convert(path);

        ProbeTaskConfigJson task = Assert.Single(config.Tasks);
        Assert.Equal("gateway", task.Name);
        Assert.Equal("192.168.1.1", task.Target);
        Assert.Equal(2000, task.Timeout);
        Assert.Equal(150, task.MaxRecentPackets);
        Assert.Equal(25, task.SignificantDelayThreshold);
        Assert.Equal(60, task.PacketLossDuration);
        Assert.Equal(8, task.PacketLossCount);
    }

    [Fact]
    public void 省略可选参数时使用默认值()
    {
        string path = Path.Combine(_directory, "config.txt");
        File.WriteAllText(path, "dns 8.8.8.8");

        ProbeTaskConfigJson task = Assert.Single(LegacyConfigConverter.Convert(path).Tasks);

        Assert.Equal(1000, task.Timeout);
        Assert.Equal(255, task.MaxRecentPackets);
        Assert.Equal(20, task.SignificantDelayThreshold);
        Assert.Equal(30, task.PacketLossDuration);
        Assert.Equal(5, task.PacketLossCount);
    }

    [Fact]
    public void 忽略空行与注释()
    {
        string path = Path.Combine(_directory, "config.txt");
        File.WriteAllText(path, "# 注释\r\n\r\n  dns 1.1.1.1  \r\n");

        Assert.Single(LegacyConfigConverter.Convert(path).Tasks);
    }

    [Fact]
    public void 空文件报错()
    {
        string path = Path.Combine(_directory, "config.txt");
        File.WriteAllText(path, "");

        Assert.Throws<InvalidOperationException>(() => LegacyConfigConverter.Convert(path));
    }

    [Fact]
    public void 转换并保存后可以加载()
    {
        string legacy = Path.Combine(_directory, "config.txt");
        string modern = Path.Combine(_directory, "config.json");
        File.WriteAllText(legacy, "dns 8.8.8.8");

        LegacyConfigConverter.ConvertAndSave(legacy, modern);

        Assert.Equal("dns", Assert.Single(ConfigLoader.LoadConfig(modern).Tasks).Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
