using Pings.Configuration;

namespace Pings.Tests;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "pings-tests-" + Guid.NewGuid().ToString("N"));

    public ConfigLoaderTests() => Directory.CreateDirectory(_directory);

    private string PathInTemp(string name) => Path.Combine(_directory, name);

    [Fact]
    public void 保存后重新加载_内容一致()
    {
        string path = PathInTemp("config.json");

        AppConfig original = new()
        {
            Tasks =
            [
                new IcmpTaskConfigJson
                {
                    Name = "内网网关",
                    IP = "192.168.1.1",
                    Timeout = 2000,
                    Interval = 500,
                    MaxRecentPackets = 64,
                    SignificantDelayThreshold = 30,
                    PacketLossDuration = 10.5,
                    PacketLossCount = 3
                }
            ],
            Global = new GlobalConfig { UiRefreshInterval = 500, NotificationCooldownSeconds = 30 },
            Logging = new LoggingConfig { LogFilePath = PathInTemp("app.log"), MaxLogFileSize = 2, MaxLogFiles = 3 }
        };

        ConfigLoader.SaveConfig(original, path);
        AppConfig loaded = ConfigLoader.LoadConfig(path);

        IcmpTaskConfigJson task = Assert.Single(loaded.Tasks);
        Assert.Equal("内网网关", task.Name);
        Assert.Equal("192.168.1.1", task.IP);
        Assert.Equal(2000, task.Timeout);
        Assert.Equal(500, task.Interval);
        Assert.Equal(10.5, task.PacketLossDuration);
        Assert.Equal(500, loaded.Global.UiRefreshInterval);
        Assert.Equal(3, loaded.Logging.MaxLogFiles);
    }

    [Fact]
    public void 文件不存在_抛出FileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => ConfigLoader.LoadConfig(PathInTemp("missing.json")));
    }

    [Fact]
    public void 非法JSON_抛出InvalidOperation()
    {
        string path = PathInTemp("broken.json");
        File.WriteAllText(path, "{ not json ");

        Assert.Throws<InvalidOperationException>(() => ConfigLoader.LoadConfig(path));
    }

    [Fact]
    public void 带BOM的JSON也能读取()
    {
        string path = PathInTemp("bom.json");
        File.WriteAllText(path, """{"tasks":[{"name":"a","ip":"1.1.1.1"}]}""", new System.Text.UTF8Encoding(true));

        AppConfig config = ConfigLoader.LoadConfig(path);

        Assert.Equal("a", Assert.Single(config.Tasks).Name);
    }

    [Fact]
    public void 默认配置本身可以通过校验()
    {
        string path = PathInTemp("default.json");
        ConfigLoader.CreateDefaultConfig(path);

        ConfigValidationResult result = ConfigValidator.Validate(ConfigLoader.LoadConfig(path));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void 保存时自动创建目录()
    {
        string path = PathInTemp(Path.Combine("nested", "config.json"));

        ConfigLoader.SaveConfig(new AppConfig { Tasks = [new IcmpTaskConfigJson { Name = "a", IP = "1.1.1.1" }] }, path);

        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
