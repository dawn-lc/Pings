using Pings.Configuration;

namespace Pings.Tests;

public class ConfigValidatorTests
{
    private static AppConfig ValidConfig() => new()
    {
        Tasks =
        [
            new IcmpTaskConfigJson { Name = "Cloudflare", IP = "1.1.1.1" },
            new IcmpTaskConfigJson { Name = "Google", IP = "8.8.8.8" }
        ]
    };

    [Fact]
    public void 合法配置_通过校验()
    {
        ConfigValidationResult result = ConfigValidator.Validate(ValidConfig());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void 空任务列表_报错()
    {
        AppConfig config = ValidConfig();
        config.Tasks.Clear();

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("至少需要一个监控任务"));
    }

    [Fact]
    public void 非法目标_报错()
    {
        AppConfig config = ValidConfig();
        config.Tasks[0].IP = "   ";

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("目标地址"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(60_001)]
    public void 超时超出范围_报错(int timeout)
    {
        AppConfig config = ValidConfig();
        config.Tasks[0].Timeout = timeout;

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("timeout"));
    }

    [Fact]
    public void 重复的目标与名称_给出警告而不是错误()
    {
        AppConfig config = ValidConfig();
        config.Tasks[1].Name = config.Tasks[0].Name;
        config.Tasks[1].IP = config.Tasks[0].IP;

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Contains("任务名称重复"));
        Assert.Contains(result.Warnings, warning => warning.Contains("目标地址重复"));
    }

    [Fact]
    public void 丢包阈值都为零_给出警告()
    {
        AppConfig config = ValidConfig();
        config.Tasks[0].PacketLossDuration = 0;
        config.Tasks[0].PacketLossCount = 0;

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Contains("丢包告警"));
    }

    [Fact]
    public void 启用Webhook但地址非法_报错()
    {
        AppConfig config = ValidConfig();
        config.Notifications.Webhook.Enabled = true;
        config.Notifications.Webhook.Url = "not-a-url";

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("webhook.url"));
    }

    [Fact]
    public void 启用邮件但缺少必填项_报错()
    {
        AppConfig config = ValidConfig();
        config.Notifications.Email.Enabled = true;

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("smtpServer"));
        Assert.Contains(result.Errors, error => error.Contains("from"));
        Assert.Contains(result.Errors, error => error.Contains("to"));
    }

    [Fact]
    public void 邮件配置合法_通过校验()
    {
        AppConfig config = ValidConfig();
        config.Notifications.Email.Enabled = true;
        config.Notifications.Email.SmtpServer = "smtp.example.com";
        config.Notifications.Email.From = "pings@example.com";
        config.Notifications.Email.To = ["ops@example.com"];

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void 界面刷新间隔过小_报错()
    {
        AppConfig config = ValidConfig();
        config.Global.UiRefreshInterval = 10;

        ConfigValidationResult result = ConfigValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("uiRefreshInterval"));
    }

    [Fact]
    public void Describe_同时输出错误与警告()
    {
        AppConfig config = ValidConfig();
        config.Tasks[0].Timeout = 0;
        config.Tasks[1].IP = config.Tasks[0].IP;

        ConfigValidationResult result = ConfigValidator.Validate(config);
        string text = ConfigValidator.Describe(result);

        Assert.Contains("✗", text);
        Assert.Contains("!", text);
    }
}
