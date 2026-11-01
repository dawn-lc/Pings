using System.Text;
using System.Text.Json;
using Pings.Properties;

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
                throw new FileNotFoundException(string.Format(Resources.ConfigFileNotFound, configPath), configPath);

            try
            {
                // 用 ReadAllText 而不是读取字节：自动跳过 UTF-8 BOM。
                string json = File.ReadAllText(configPath, Encoding.UTF8);
                return JsonSerializer.Deserialize(json, JsonContext.Relaxed.AppConfig)
                    ?? throw new JsonException(Resources.ConfigContentEmpty);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(string.Format(Resources.ConfigParseFailed, ex.Message), ex);
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
                throw new InvalidOperationException(string.Format(Resources.ConfigSaveFailed, ex.Message), ex);
            }
        }

        /// <summary>
        /// 生成默认配置文件。
        /// </summary>
        /// <remarks>
        /// 默认任务全部指向 Cloudflare 的公共测试端点，开箱即可验证连通性、延迟与抖动。
        /// 每个端点的依据（均核对过官方文档，并在本机实测过状态码）：
        /// <list type="bullet">
        /// <item><c>1.1.1.1</c> / <c>1.0.0.1</c>：官方 <c>1.1.1.1</c> 解析器地址，响应 ICMP；
        ///   文档见 <c>developers.cloudflare.com/1.1.1.1/ip-addresses/</c>。</item>
        /// <item><c>1.1.1.1:443</c>：DoH 端口，官方文档明确 DoH 走 443。</item>
        /// <item><c>1.1.1.1:853</c>：DoT 标准端口，官方文档明确支持
        ///   <c>1.1.1.1</c> / <c>1.0.0.1</c> 与主机名 <c>one.one.one.one</c>。</item>
        /// <item><c>cp.cloudflare.com/generate_204</c>：通用 captive portal 检测端点，
        ///   HEAD / GET、HTTP / HTTPS 均固定返回 204；官方 1.1.1.1 文档未列出该路径，
        ///   但它是 Android / ChromeOS 判断「网络是否真的通了」的经典端点，实测稳定。</item>
        /// <item><c>one.one.one.one/help/</c>：官方文档「Verify connection」推荐的诊断页。
        ///   注意末尾必须带斜杠——不带会返回 308 重定向。</item>
        /// <item><c>1.1.1.1/cdn-cgi/trace</c>：返回出口 IP 与线路信息，可用来确认走的是哪个节点；
        ///   它不实现 HEAD（返回 302），必须用 GET。</item>
        /// </list>
        ///
        /// 刻意不放 UDP 任务：Cloudflare 没有任何公开的 UDP 回声服务。
        /// 实测 <c>time.cloudflare.com:123</c>（官方 NTP）与 <c>1.1.1.1:53</c>（DNS）对本程序
        /// 发送的文本探测包都不会回应——它们是协议服务端，不是回声服务，只能恒定得到「无回应」。
        /// 需要 UDP 监控时请指向自己的回声服务（见 README「UDP 的语义边界」）。
        ///
        /// 超时给得比较宽：这些端点在本机实测往返约 250~1000ms，
        /// 用 1000ms 容易把偶发抖动误判成超时。
        /// 间隔统一设为与超时相同：小于超时会触发「实际探测间隔会被探测耗时撑长」的配置警告，
        /// 默认配置首次运行应该干净无警告。
        /// </remarks>
        public static AppConfig CreateDefaultConfig(string configPath)
        {
            AppConfig defaultConfig = new() { Tasks = DefaultTasks() };

            SaveConfig(defaultConfig, configPath);
            return defaultConfig;
        }

        /// <summary>默认任务清单。</summary>
        private static List<ProbeTaskConfigJson> DefaultTasks() =>
        [
            new()
            {
                Name = Resources.DefaultTaskDns,
                Protocol = "icmp",
                Target = "1.1.1.1",
                Timeout = 3000,
                Interval = 3000
            },
            new()
            {
                Name = Resources.DefaultTaskBackupDns,
                Protocol = "icmp",
                Target = "1.0.0.1",
                Timeout = 3000,
                Interval = 3000
            },
            new()
            {
                Name = Resources.DefaultTaskDoh,
                Protocol = "tcp",
                Target = "1.1.1.1",
                Port = 443,
                Timeout = 3000,
                Interval = 3000
            },
            new()
            {
                Name = Resources.DefaultTaskDot,
                Protocol = "tcp",
                Target = "1.1.1.1",
                Port = 853,
                Timeout = 3000,
                Interval = 3000
            },
            new()
            {
                Name = Resources.DefaultTaskConnectivity,
                Protocol = "http",
                Target = "https://cp.cloudflare.com/generate_204",
                Timeout = 5000,
                Interval = 5000,

                // 该端点按约定固定返回 204，显式写出来比依赖「200~399 都算成功」更清楚。
                ExpectedStatus = [204]
            },
            new()
            {
                Name = Resources.DefaultTaskDiagnostics,
                Protocol = "http",
                Target = "https://one.one.one.one/help/",
                Timeout = 5000,
                Interval = 5000,
                ExpectedStatus = [200]
            },
            new()
            {
                Name = Resources.DefaultTaskTrace,
                Protocol = "http",
                Target = "https://1.1.1.1/cdn-cgi/trace",

                // cdn-cgi/trace 不实现 HEAD（返回 302 重定向），必须用 GET。
                HttpMethod = "GET",
                Timeout = 5000,
                Interval = 5000,
                ExpectedStatus = [200]
            }
        ];
    }
}
