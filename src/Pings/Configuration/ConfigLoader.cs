using System.Text;
using System.Text.Json;

namespace Pings.Configuration
{
    /// <summary>
    /// JSON 配置文件的加载、保存与默认配置生成。
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>写入配置时使用的编码（UTF-8 无 BOM）。</summary>
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        /// <summary>
        /// 从文件加载配置。
        /// </summary>
        /// <exception cref="FileNotFoundException">文件不存在。</exception>
        /// <exception cref="InvalidOperationException">内容无法解析。</exception>
        public static AppConfig LoadConfig(string configPath)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"配置文件不存在: {configPath}", configPath);

            try
            {
                // 用 ReadAllText 而不是读取字节：自动跳过 UTF-8 BOM。
                string json = File.ReadAllText(configPath, Encoding.UTF8);
                return JsonSerializer.Deserialize(json, JsonContext.Relaxed.AppConfig)
                    ?? throw new JsonException("配置文件内容为空");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"配置文件解析失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 原子保存配置：先写临时文件，再整体替换，避免中途失败留下半个文件。
        /// </summary>
        public static void SaveConfig(AppConfig config, string configPath)
        {
            try
            {
                string fullPath = Path.GetFullPath(configPath);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(config, JsonContext.Relaxed.AppConfig);
                string tempPath = fullPath + ".tmp";

                File.WriteAllText(tempPath, json, Utf8NoBom);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new InvalidOperationException($"配置文件保存失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 生成默认配置文件。
        /// </summary>
        public static AppConfig CreateDefaultConfig(string configPath)
        {
            AppConfig defaultConfig = new()
            {
                Tasks =
                [
                    new IcmpTaskConfigJson
                    {
                        Name = "Google DNS",
                        IP = "8.8.8.8"
                    },
                    new IcmpTaskConfigJson
                    {
                        Name = "Cloudflare DNS",
                        IP = "1.1.1.1"
                    }
                ]
            };

            SaveConfig(defaultConfig, configPath);
            return defaultConfig;
        }
    }
}
