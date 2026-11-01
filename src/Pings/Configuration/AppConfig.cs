using System.Text.Json.Serialization;

namespace Pings.Configuration
{
    /// <summary>
    /// JSON 配置文件根模型。
    /// </summary>
    public sealed class AppConfig
    {
        /// <summary>监控任务列表。</summary>
        [JsonPropertyName("tasks")]
        public List<ProbeTaskConfigJson> Tasks { get; set; } = [];

        /// <summary>全局行为配置。</summary>
        [JsonPropertyName("global")]
        public GlobalConfig Global { get; set; } = new();

        /// <summary>日志配置。</summary>
        [JsonPropertyName("logging")]
        public LoggingConfig Logging { get; set; } = new();

        /// <summary>通知配置（Webhook / Email）。</summary>
        [JsonPropertyName("notifications")]
        public NotificationsConfig Notifications { get; set; } = new();
    }

    /// <summary>
    /// 单个监控任务的 JSON 配置。
    /// </summary>
    public sealed class ProbeTaskConfigJson
    {
        /// <summary>任务名称（界面显示名）。</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>探测协议：icmp / tcp / udp / http，默认 icmp。</summary>
        [JsonPropertyName("protocol")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Protocol { get; set; }

        /// <summary>
        /// 目标：icmp / tcp / udp 为主机名或 IP；http 为完整 URL。必填。
        /// </summary>
        [JsonPropertyName("target")]
        public string Target { get; set; } = string.Empty;

        /// <summary>端口，tcp / udp 必填。</summary>
        [JsonPropertyName("port")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Port { get; set; }

        /// <summary>单次探测超时时间（毫秒），默认 1000。</summary>
        [JsonPropertyName("timeout")]
        public int Timeout { get; set; } = 1000;

        /// <summary>探测间隔（毫秒），0 表示与 <see cref="Timeout"/> 相同。</summary>
        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        /// <summary>参与丢包率与延迟统计的样本数（滑动窗口），默认 255。</summary>
        [JsonPropertyName("maxRecentPackets")]
        public int MaxRecentPackets { get; set; } = 255;

        /// <summary>显著延迟波动阈值（毫秒），0 表示关闭，默认 20。</summary>
        [JsonPropertyName("significantDelayThreshold")]
        public int SignificantDelayThreshold { get; set; } = 20;

        /// <summary>连续无响应持续时间阈值（秒），0 表示不按时长判定，默认 30。</summary>
        [JsonPropertyName("packetLossDuration")]
        public double PacketLossDuration { get; set; } = 30.0;

        /// <summary>连续无响应次数阈值，0 表示不按次数判定，默认 5。</summary>
        [JsonPropertyName("packetLossCount")]
        public int PacketLossCount { get; set; } = 5;

        /// <summary>短故障统计窗口（秒），0 表示关闭抖动链路检测，默认 300。</summary>
        [JsonPropertyName("lossFlapWindowSeconds")]
        public double LossFlapWindowSeconds { get; set; } = 300;

        /// <summary>窗口内短故障达到多少次判定为抖动链路，默认 3。</summary>
        [JsonPropertyName("lossFlapCount")]
        public int LossFlapCount { get; set; } = 3;

        /// <summary>ICMP 负载字节数，默认 32。</summary>
        [JsonPropertyName("payloadSize")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? PayloadSize { get; set; }

        /// <summary>UDP 探测负载文本，默认 "Pings"。</summary>
        [JsonPropertyName("udpPayload")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UdpPayload { get; set; }

        /// <summary>HTTP 请求方法，仅 HEAD / GET，默认 HEAD。</summary>
        [JsonPropertyName("httpMethod")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HttpMethod { get; set; }

        /// <summary>HTTP 期望状态码，空表示 200~399。</summary>
        [JsonPropertyName("expectedStatus")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int>? ExpectedStatus { get; set; }

        /// <summary>HTTP 是否跟随重定向，默认 false。</summary>
        [JsonPropertyName("followRedirects")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool FollowRedirects { get; set; }

        /// <summary>HTTP 是否复用连接池，默认 false（每轮新建连接，RTT 可比）。</summary>
        [JsonPropertyName("reuseConnection")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool ReuseConnection { get; set; }

        /// <summary>HTTP 是否使用系统代理，默认 false。</summary>
        [JsonPropertyName("useProxy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool UseProxy { get; set; }
    }

    /// <summary>
    /// 全局行为配置。
    /// </summary>
    public sealed class GlobalConfig
    {
        /// <summary>自动确认警告的间隔（秒），0 表示禁用，默认 0。</summary>
        [JsonPropertyName("autoConfirmWarningInterval")]
        public int AutoConfirmWarningInterval { get; set; } = 0;

        /// <summary>界面刷新间隔（毫秒），默认 1000。</summary>
        [JsonPropertyName("uiRefreshInterval")]
        public int UiRefreshInterval { get; set; } = 1000;

        /// <summary>输出详细日志，默认 false。</summary>
        [JsonPropertyName("enableVerboseLogging")]
        public bool EnableVerboseLogging { get; set; } = false;

        /// <summary>
        /// 同一目标的通知冷却时间（秒），用于抑制告警风暴，0 表示不限制，默认 60。
        /// </summary>
        [JsonPropertyName("notificationCooldownSeconds")]
        public int NotificationCooldownSeconds { get; set; } = 60;

        /// <summary>目标恢复连通时是否也发送通知，默认 true。</summary>
        [JsonPropertyName("notifyOnRecovery")]
        public bool NotifyOnRecovery { get; set; } = true;
    }

    /// <summary>
    /// 日志配置。
    /// </summary>
    public sealed class LoggingConfig
    {
        /// <summary>日志文件路径，默认 "Pings.log"。</summary>
        [JsonPropertyName("logFilePath")]
        public string LogFilePath { get; set; } = "Pings.log";

        /// <summary>单个日志文件的最大体积（MB），默认 10。</summary>
        [JsonPropertyName("maxLogFileSize")]
        public int MaxLogFileSize { get; set; } = 10;

        /// <summary>保留的历史日志文件数量，默认 5。</summary>
        [JsonPropertyName("maxLogFiles")]
        public int MaxLogFiles { get; set; } = 5;

        /// <summary>同时输出到控制台，默认 true（实时表格运行时自动抑制）。</summary>
        [JsonPropertyName("enableConsoleLog")]
        public bool EnableConsoleLog { get; set; } = true;
    }

    /// <summary>
    /// 通知配置。
    /// </summary>
    public sealed class NotificationsConfig
    {
        [JsonPropertyName("webhook")]
        public WebhookConfig Webhook { get; set; } = new();

        [JsonPropertyName("email")]
        public EmailConfig Email { get; set; } = new();
    }

    /// <summary>
    /// Webhook 通知配置。
    /// </summary>
    public sealed class WebhookConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("method")]
        public string Method { get; set; } = "POST";

        /// <summary>附加请求头，值支持与 <see cref="Content"/> 相同的模板占位符。</summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; } = [];

        /// <summary>认证方案（如 Bearer / Basic），留空表示不认证。</summary>
        [JsonPropertyName("authType")]
        public string AuthType { get; set; } = string.Empty;

        [JsonPropertyName("authToken")]
        public string AuthToken { get; set; } = string.Empty;

        /// <summary>请求体模板，支持 %#Key#% 形式的占位符。</summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// 邮件通知配置。
    /// </summary>
    public sealed class EmailConfig
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
}
