using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pings
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(List<ICMPTaskConfigJson>))]
    [JsonSerializable(typeof(ICMPTaskConfigJson))]
    [JsonSerializable(typeof(GlobalConfig))]
    [JsonSerializable(typeof(LoggingConfig))]
    [JsonSerializable(typeof(NotificationsConfig))]
    [JsonSerializable(typeof(WebhookConfig))]
    [JsonSerializable(typeof(EmailConfig))]
    [JsonSerializable(typeof(WebhookPayload))]
    internal partial class JsonContext : JsonSerializerContext
    {
        private static readonly Lazy<JsonContext> relaxed = new(() =>
            new JsonContext(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

        public static JsonContext Relaxed => relaxed.Value;
    }

    /// <summary>
    /// JSON配置文件加载器（AOT兼容）
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>
        /// 从文件加载配置
        /// </summary>
        /// <param name="configPath">配置文件路径</param>
        /// <returns>应用程序配置</returns>
        public static AppConfig LoadConfig(string configPath)
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"配置文件不存在: {configPath}");
            }

            try
            {
                byte[] jsonBytes = File.ReadAllBytes(configPath);
                return JsonSerializer.Deserialize(jsonBytes, JsonContext.Relaxed.AppConfig) ?? throw new JsonException("配置文件内容为空或格式错误");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"配置文件解析失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 保存配置到文件
        /// </summary>
        /// <param name="config">应用程序配置</param>
        /// <param name="configPath">配置文件路径</param>
        public static void SaveConfig(AppConfig config, string configPath)
        {
            try
            {
                string jsonContent = JsonSerializer.Serialize(config, JsonContext.Relaxed.AppConfig);
                File.WriteAllText(configPath, jsonContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"配置文件保存失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 创建默认配置文件
        /// </summary>
        public static void CreateDefaultConfig(string configPath)
        {
            var defaultConfig = new AppConfig
            {
                Tasks =
                [
                    new ICMPTaskConfigJson
                    {
                        Name = "Google DNS",
                        IP = "8.8.8.8",
                        Timeout = 1000,
                        MaxRecentPackets = 255,
                        SignificantDelayThreshold = 20,
                        PacketLossDuration = 30.0,
                        PacketLossCount = 5
                    },
                    new ICMPTaskConfigJson
                    {
                        Name = "Cloudflare DNS",
                        IP = "1.1.1.1",
                        Timeout = 1000,
                        MaxRecentPackets = 255,
                        SignificantDelayThreshold = 20,
                        PacketLossDuration = 30.0,
                        PacketLossCount = 5
                    }
                ],
                Logging = new LoggingConfig { LogFilePath = "Pings.log" },
                Notifications = new NotificationsConfig()
            };

            SaveConfig(defaultConfig, configPath);
        }

        /// <summary>
        /// 验证配置
        /// </summary>
        public static (bool IsValid, string ErrorMessage) ValidateConfig(AppConfig config)
        {
            if (config?.Tasks == null || config.Tasks.Count == 0)
                return (false, "至少需要一个监控任务");

            foreach (var task in config.Tasks)
            {
                if (string.IsNullOrWhiteSpace(task.Name))
                    return (false, "任务名称不能为空");
                if (string.IsNullOrWhiteSpace(task.IP))
                    return (false, "IP地址不能为空");
            }

            // 验证邮件配置
            if (config.Notifications?.Email?.Enabled ?? false)
            {
                var email = config.Notifications.Email;

                if (string.IsNullOrWhiteSpace(email.SmtpServer))
                    return (false, "邮件SMTP服务器地址不能为空");

                if (email.Port <= 0 || email.Port > 65535)
                    return (false, "邮件SMTP端口号无效(1-65535)");

                if (string.IsNullOrWhiteSpace(email.From))
                    return (false, "邮件发件人地址不能为空");

                if (email.To?.Count == 0)
                    return (false, "邮件收件人地址不能为空");

                if (!string.IsNullOrWhiteSpace(email.Username) && string.IsNullOrWhiteSpace(email.Password))
                    return (false, "邮件需要认证但密码为空");
            }

            return (true, "");
        }

        /// <summary>
        /// 将JSON任务配置转换为 ICMPTaskConfig
        /// </summary>
        public static ICMPTaskConfig ToICMPTaskConfig(ICMPTaskConfigJson jsonConfig)
        {
            return new ICMPTaskConfig(
                jsonConfig.Name,
                jsonConfig.IP,
                jsonConfig.Timeout,
                jsonConfig.MaxRecentPackets,
                jsonConfig.SignificantDelayThreshold,
                jsonConfig.PacketLossDuration,
                jsonConfig.PacketLossCount
            );
        }
    }
}