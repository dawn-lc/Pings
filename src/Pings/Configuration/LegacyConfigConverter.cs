using System.Globalization;
using System.Text;

namespace Pings.Configuration
{
    /// <summary>
    /// 旧版以空格分隔的 config.txt 迁移为 JSON。
    /// 格式：&lt;名称&gt; &lt;IP/域名&gt; [超时] [样本数] [延迟阈值] [丢包时长] [丢包次数]
    /// </summary>
    public static class LegacyConfigConverter
    {
        public static AppConfig Convert(string oldConfigPath)
        {
            string[] lines = ReadAllLines(oldConfigPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToArray();

            if (lines.Length == 0)
                throw new InvalidOperationException("旧版配置文件为空或格式错误");

            AppConfig config = new() { Tasks = [] };

            foreach (string line in lines)
            {
                string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    throw new InvalidOperationException($"无法解析的配置行: {line}");

                config.Tasks.Add(new IcmpTaskConfigJson
                {
                    Name = parts[0],
                    IP = parts[1],
                    Timeout = ParseInt(parts, 2, 1000),
                    MaxRecentPackets = ParseInt(parts, 3, 255),
                    SignificantDelayThreshold = ParseInt(parts, 4, 20),
                    PacketLossDuration = ParseDouble(parts, 5, 30.0),
                    PacketLossCount = ParseInt(parts, 6, 5)
                });
            }

            return config;
        }

        /// <summary>迁移并保存，返回生成的配置对象。</summary>
        public static AppConfig ConvertAndSave(string oldConfigPath, string newConfigPath)
        {
            AppConfig config = Convert(oldConfigPath);
            ConfigLoader.SaveConfig(config, newConfigPath);
            return config;
        }

        private static int ParseInt(string[] parts, int index, int defaultValue)
            => parts.Length > index && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : defaultValue;

        private static double ParseDouble(string[] parts, int index, double defaultValue)
            => parts.Length > index && double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : defaultValue;

        /// <summary>
        /// 读取旧文本配置文件：优先严格按 UTF-8 解码，失败后按 GBK（936）重试。
        /// 直接使用 Encoding.Default 在 .NET Core 上恒为 UTF-8，会把简中 Windows 记事本
        /// 另存的 ANSI 文件读成乱码，迁移出来的任务名不可用。
        /// </summary>
        private static string[] ReadAllLines(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            try
            {
                string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
                return SplitLines(text);
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return SplitLines(Encoding.GetEncoding(936).GetString(bytes));
            }
        }

        private static string[] SplitLines(string text)
            => text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
    }
}
