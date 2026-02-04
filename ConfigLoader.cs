using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pings
{
    /// <summary>
    /// JSON配置文件加载器（AOT兼容）
    /// </summary>
    public static class ConfigLoader
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // 为AOT编译启用源生成
            TypeInfoResolver = JsonContext.Default
        };

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
                string jsonContent = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize(jsonContent, JsonContext.Default.AppConfig)
                    ?? throw new JsonException("配置文件内容为空或格式错误");
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
                string jsonContent = JsonSerializer.Serialize(config, JsonContext.Default.AppConfig);
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
        /// <param name="configPath">配置文件路径</param>
        public static void CreateDefaultConfig(string configPath)
        {
            var defaultConfig = new AppConfig
            {
                Tasks = new List<ICMPTaskConfigJson>
                {
                    new ICMPTaskConfigJson
                    {
                        Name = "本机",
                        IP = "127.0.0.1",
                        Timeout = 1000,
                        MaxRecentPackets = 255,
                        SignificantDelayThreshold = 20,
                        PacketLossDuration = 30.0,
                        PacketLossCount = 5
                    },
                    new ICMPTaskConfigJson
                    {
                        Name = "网关",
                        IP = "192.168.1.1",
                        Timeout = 1000,
                        MaxRecentPackets = 255,
                        SignificantDelayThreshold = 20,
                        PacketLossDuration = 30.0,
                        PacketLossCount = 5
                    },
                    new ICMPTaskConfigJson
                    {
                        Name = "DNS服务器",
                        IP = "8.8.8.8",
                        Timeout = 1000,
                        MaxRecentPackets = 255,
                        SignificantDelayThreshold = 20,
                        PacketLossDuration = 30.0,
                        PacketLossCount = 5
                    }
                },
                Global = new GlobalConfig
                {
                    AutoConfirmWarningInterval = 0,
                    UiRefreshInterval = 1000,
                    EnableVerboseLogging = false
                },
                Logging = new LoggingConfig
                {
                    LogFilePath = "Pings.log",
                    MaxLogFileSize = 10,
                    MaxLogFiles = 5,
                    EnableConsoleLog = true
                }
            };

            SaveConfig(defaultConfig, configPath);
        }

        /// <summary>
        /// 将JSON配置转换为ICMPTaskConfig结构
        /// </summary>
        /// <param name="jsonConfig">JSON配置</param>
        /// <returns>ICMPTaskConfig结构</returns>
        public static ICMPTaskConfig ToICMPTaskConfig(ICMPTaskConfigJson jsonConfig)
        {
            return new ICMPTaskConfig(
                jsonConfig.Name,
                jsonConfig.IP,
                jsonConfig.Timeout.ToString(),
                jsonConfig.MaxRecentPackets.ToString(),
                jsonConfig.SignificantDelayThreshold.ToString(),
                jsonConfig.PacketLossDuration.ToString(),
                jsonConfig.PacketLossCount.ToString()
            );
        }

        /// <summary>
        /// 验证配置文件
        /// </summary>
        /// <param name="config">应用程序配置</param>
        /// <returns>验证结果和错误消息</returns>
        public static (bool IsValid, string ErrorMessage) ValidateConfig(AppConfig config)
        {
            if (config == null)
            {
                return (false, "配置对象为空");
            }

            if (config.Tasks == null || config.Tasks.Count == 0)
            {
                return (false, "监控任务列表为空");
            }

            foreach (var task in config.Tasks)
            {
                if (string.IsNullOrWhiteSpace(task.Name))
                {
                    return (false, "任务名称不能为空");
                }

                if (string.IsNullOrWhiteSpace(task.IP))
                {
                    return (false, $"任务 '{task.Name}' 的IP地址不能为空");
                }

                if (task.Timeout <= 0)
                {
                    return (false, $"任务 '{task.Name}' 的超时时间必须大于0");
                }

                if (task.MaxRecentPackets <= 0)
                {
                    return (false, $"任务 '{task.Name}' 的最大记录数必须大于0");
                }

                if (task.SignificantDelayThreshold < 0)
                {
                    return (false, $"任务 '{task.Name}' 的延迟变化阈值不能为负数");
                }

                if (task.PacketLossDuration <= 0)
                {
                    return (false, $"任务 '{task.Name}' 的丢包持续时间必须大于0");
                }

                if (task.PacketLossCount <= 0)
                {
                    return (false, $"任务 '{task.Name}' 的丢包计数阈值必须大于0");
                }
            }

            if (config.Global == null)
            {
                return (false, "全局配置为空");
            }

            if (config.Global.UiRefreshInterval <= 0)
            {
                return (false, "界面刷新间隔必须大于0");
            }

            if (config.Logging == null)
            {
                return (false, "日志配置为空");
            }

            if (string.IsNullOrWhiteSpace(config.Logging.LogFilePath))
            {
                return (false, "日志文件路径不能为空");
            }

            if (config.Logging.MaxLogFileSize <= 0)
            {
                return (false, "最大日志文件大小必须大于0");
            }

            if (config.Logging.MaxLogFiles <= 0)
            {
                return (false, "保留的日志文件数量必须大于0");
            }

            return (true, string.Empty);
        }
    }

    /// <summary>
    /// JSON序列化上下文（AOT必需）
    /// </summary>
    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(List<ICMPTaskConfigJson>))]
    [JsonSerializable(typeof(ICMPTaskConfigJson))]
    [JsonSerializable(typeof(GlobalConfig))]
    [JsonSerializable(typeof(LoggingConfig))]
    internal partial class JsonContext : JsonSerializerContext
    {
    }
}