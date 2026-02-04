using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pings
{
    /// <summary>
    /// JSON配置文件模型
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 监控任务配置列表
        /// </summary>
        [JsonPropertyName("tasks")]
        public List<ICMPTaskConfigJson> Tasks { get; set; } = [];

        /// <summary>
        /// 全局配置
        /// </summary>
        [JsonPropertyName("global")]
        public GlobalConfig Global { get; set; } = new();

        /// <summary>
        /// 日志配置
        /// </summary>
        [JsonPropertyName("logging")]
        public LoggingConfig Logging { get; set; } = new();

        /// <summary>
        /// 通知配置（Webhook / Email）
        /// </summary>
        [JsonPropertyName("notifications")]
        public NotificationsConfig Notifications { get; set; } = new();
    }

    /// <summary>
    /// ICMP任务配置（JSON版本）
    /// </summary>
    public class ICMPTaskConfigJson
    {
        /// <summary>
        /// 任务名称
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 目标IP地址或域名
        /// </summary>
        [JsonPropertyName("ip")]
        public string IP { get; set; } = string.Empty;

        /// <summary>
        /// Ping超时时间（毫秒），默认1000
        /// </summary>
        [JsonPropertyName("timeout")]
        public int Timeout { get; set; } = 1000;

        /// <summary>
        /// 最大最近数据包记录数，默认255
        /// </summary>
        [JsonPropertyName("maxRecentPackets")]
        public int MaxRecentPackets { get; set; } = 255;

        /// <summary>
        /// 显著延迟变化阈值（毫秒），默认20
        /// </summary>
        [JsonPropertyName("significantDelayThreshold")]
        public int SignificantDelayThreshold { get; set; } = 20;

        /// <summary>
        /// 丢包持续时间阈值（秒），默认30
        /// </summary>
        [JsonPropertyName("packetLossDuration")]
        public double PacketLossDuration { get; set; } = 30.0;

        /// <summary>
        /// 丢包计数阈值，默认5
        /// </summary>
        [JsonPropertyName("packetLossCount")]
        public int PacketLossCount { get; set; } = 5;
    }

    /// <summary>
    /// 全局配置
    /// </summary>
    public class GlobalConfig
    {
        /// <summary>
        /// 自动确认警告的间隔（秒），0表示禁用，默认0
        /// </summary>
        [JsonPropertyName("autoConfirmWarningInterval")]
        public int AutoConfirmWarningInterval { get; set; } = 0;

        /// <summary>
        /// 界面刷新间隔（毫秒），默认1000
        /// </summary>
        [JsonPropertyName("uiRefreshInterval")]
        public int UiRefreshInterval { get; set; } = 1000;

        /// <summary>
        /// 启用详细日志，默认false
        /// </summary>
        [JsonPropertyName("enableVerboseLogging")]
        public bool EnableVerboseLogging { get; set; } = false;
    }

    /// <summary>
    /// 日志配置
    /// </summary>
    public class LoggingConfig
    {
        /// <summary>
        /// 日志文件路径，默认"Pings.log"
        /// </summary>
        [JsonPropertyName("logFilePath")]
        public string LogFilePath { get; set; } = "Pings.log";

        /// <summary>
        /// 最大日志文件大小（MB），默认10
        /// </summary>
        [JsonPropertyName("maxLogFileSize")]
        public int MaxLogFileSize { get; set; } = 10;

        /// <summary>
        /// 保留的日志文件数量，默认5
        /// </summary>
        [JsonPropertyName("maxLogFiles")]
        public int MaxLogFiles { get; set; } = 5;

        /// <summary>
        /// 启用控制台日志，默认true
        /// </summary>
        [JsonPropertyName("enableConsoleLog")]
        public bool EnableConsoleLog { get; set; } = true;
    }

    /// <summary>
    /// 通知配置（Webhook 与 Email）
    /// </summary>
    public class NotificationsConfig
    {
        [JsonPropertyName("webhook")]
        public WebhookConfig Webhook { get; set; } = new();

        [JsonPropertyName("email")]
        public EmailConfig Email { get; set; } = new();
    }

    public class WebhookConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("method")]
        public string Method { get; set; } = "POST";

        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; } = [];

        // 认证类型，例如 "Bearer"；如果非空且 authToken 提供，将在请求中添加 Authorization 头
        [JsonPropertyName("authType")]
        public string AuthType { get; set; } = string.Empty;

        [JsonPropertyName("authToken")]
        public string AuthToken { get; set; } = string.Empty;
    }

    public class EmailConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("smtpServer")]
        public string SmtpServer { get; set; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; set; } = 25;

        [JsonPropertyName("enableSsl")]
        public bool EnableSsl { get; set; } = true;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("to")]
        public List<string> To { get; set; } = [];
    }

    /// <summary>
    /// Webhook 通知 payload
    /// </summary>
    public class WebhookPayload
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("ip")]
        public string? IP { get; set; }

        [JsonPropertyName("previousState")]
        public string? PreviousState { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("delayMs")]
        public int? DelayMs { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
    }
}