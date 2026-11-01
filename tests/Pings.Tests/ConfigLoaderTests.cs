using System.Text.Json.Nodes;
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
                new ProbeTaskConfigJson
                {
                    Name = "内网网关",
                    Target = "192.168.1.1",
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

        ProbeTaskConfigJson task = Assert.Single(loaded.Tasks);
        Assert.Equal("内网网关", task.Name);
        Assert.Equal("192.168.1.1", task.Target);
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
        File.WriteAllText(path, """{"tasks":[{"name":"a","target":"1.1.1.1"}]}""", new System.Text.UTF8Encoding(true));

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

        // 默认配置首次运行应该是干净的：有警告说明默认值配得不合理（例如 interval < timeout）。
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void 默认配置覆盖多种协议且都不依赖UDP()
    {
        string path = PathInTemp("default-protocols.json");
        AppConfig config = ConfigLoader.CreateDefaultConfig(path);

        HashSet<string> protocols = [.. config.Tasks.Select(task => task.Protocol ?? "icmp")];

        Assert.Contains("icmp", protocols);
        Assert.Contains("tcp", protocols);
        Assert.Contains("http", protocols);

        // Cloudflare 没有公开的 UDP 回声服务，默认任务里放 UDP 只会恒定报「无回应」。
        Assert.DoesNotContain("udp", protocols);

        // 每个 HTTP 任务都应显式声明期望状态码，否则 3xx 也会被当成成功。
        foreach (ProbeTaskConfigJson task in config.Tasks.Where(t => t.Protocol == "http"))
            Assert.NotEmpty(task.ExpectedStatus ?? []);
    }

    [Fact]
    public void 默认配置生成的文件只包含相关字段()
    {
        string path = PathInTemp("default-lean.json");
        ConfigLoader.CreateDefaultConfig(path);

        JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
        JsonArray tasks = root["tasks"]!.AsArray();

        foreach (JsonNode? node in tasks)
        {
            JsonObject task = node!.AsObject();
            string protocol = task["protocol"]?.GetValue<string>() ?? "icmp";

            // 这两种字段只在对应协议上有意义，写进其他任务只是噪音。
            Assert.Null(task["udpPayload"]);
            Assert.Null(task["payloadSize"]);

            if (protocol == "http")
            {
                Assert.NotNull(task["expectedStatus"]);
                Assert.Null(task["port"]);
            }
            else
            {
                Assert.Null(task["httpMethod"]);
                Assert.Null(task["expectedStatus"]);
            }

            if (protocol == "tcp")
                Assert.NotNull(task["port"]);
            else
                Assert.Null(task["port"]);
        }

        // 三个默认 false 的 HTTP 开关不应出现在文件里。
        Assert.DoesNotContain("\"followRedirects\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void 保存时自动创建目录()
    {
        string path = PathInTemp(Path.Combine("nested", "config.json"));

        ConfigLoader.SaveConfig(new AppConfig { Tasks = [new ProbeTaskConfigJson { Name = "a", Target = "1.1.1.1" }] }, path);

        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
