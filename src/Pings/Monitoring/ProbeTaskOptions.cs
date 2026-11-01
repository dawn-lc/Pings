using System.Net;
using System.Text;
using Pings.Configuration;

namespace Pings.Monitoring
{
    /// <summary>
    /// 单个监控任务的运行期参数（已校验、不可变）。
    /// </summary>
    public sealed class ProbeTaskOptions
    {
        public const int DefaultTimeoutMs = 1000;
        public const int DefaultSampleWindow = 255;
        public const int DefaultSignificantDelayThresholdMs = 20;
        public const double DefaultPacketLossDurationSeconds = 30;
        public const int DefaultPacketLossCount = 5;
        public const double DefaultLossFlapWindowSeconds = 300;
        public const int DefaultLossFlapCount = 3;
        public const int DefaultPayloadSize = 32;

        /// <summary>延迟基线的预热样本数，未达到前不判定波动。</summary>
        public const int DelaySpikeWarmupSamples = 5;

        /// <summary>连续多少个超限样本才确认延迟波动。</summary>
        public const int DelaySpikeConfirmSamples = 2;

        /// <summary>任务名称。</summary>
        public required string Name { get; init; }

        /// <summary>探测协议。</summary>
        public ProbeProtocol Protocol { get; init; } = ProbeProtocol.Icmp;

        /// <summary>
        /// 目标：icmp / tcp / udp 为主机名或 IP；http 为完整 URL。
        /// </summary>
        public required string Target { get; init; }

        /// <summary>端口，tcp / udp 必填。</summary>
        public int Port { get; init; }

        /// <summary>单次探测超时（毫秒）。</summary>
        public int TimeoutMs { get; init; } = DefaultTimeoutMs;

        /// <summary>两次探测之间的间隔（毫秒），0 表示与超时时间相同。</summary>
        public int IntervalMs { get; init; }

        /// <summary>滑窗样本数，同时用于丢包率与 RTT 统计。</summary>
        public int SampleWindow { get; init; } = DefaultSampleWindow;

        /// <summary>显著延迟波动阈值（毫秒），0 表示关闭。</summary>
        public int SignificantDelayThresholdMs { get; init; } = DefaultSignificantDelayThresholdMs;

        /// <summary>连续丢包持续时长阈值（秒），0 表示不按时长判定。</summary>
        public double PacketLossDurationSeconds { get; init; } = DefaultPacketLossDurationSeconds;

        /// <summary>连续丢包次数阈值，0 表示不按次数判定。</summary>
        public int PacketLossCount { get; init; } = DefaultPacketLossCount;

        /// <summary>短故障统计窗口（秒），0 表示关闭抖动链路检测。</summary>
        public double LossFlapWindowSeconds { get; init; } = DefaultLossFlapWindowSeconds;

        /// <summary>窗口内短故障达到多少次判定为抖动链路。</summary>
        public int LossFlapCount { get; init; } = DefaultLossFlapCount;

        /// <summary>ICMP 负载字节数。</summary>
        public int PayloadSize { get; init; } = DefaultPayloadSize;

        /// <summary>
        /// UDP 探测负载的原始配置值：纯文本、<c>base64:…</c> 或 <c>file:…</c>。
        /// 保留原值是为了 JSON 往返不丢信息。
        /// </summary>
        public string UdpPayload { get; init; } = UdpPayloadResolver.DefaultText;

        /// <summary>UDP 探测实际发送的字节（已按 <see cref="UdpPayloadResolver" /> 规则解析）。</summary>
        public byte[] UdpPayloadBytes { get; init; } = Encoding.UTF8.GetBytes(UdpPayloadResolver.DefaultText);

        /// <summary>HTTP 请求方法，仅 HEAD / GET。</summary>
        public string HttpMethod { get; init; } = "HEAD";

        /// <summary>HTTP 期望状态码，空表示 200~399。</summary>
        public IReadOnlyList<int> ExpectedStatus { get; init; } = [];

        /// <summary>HTTP 是否跟随重定向。</summary>
        public bool FollowRedirects { get; init; }

        /// <summary>HTTP 是否复用连接池（默认每轮新建连接，保证 RTT 可比）。</summary>
        public bool ReuseConnection { get; init; }

        /// <summary>HTTP 是否使用系统代理（默认不使用）。</summary>
        public bool UseProxy { get; init; }

        /// <summary>实际探测间隔：未显式配置时与超时时间一致。</summary>
        public int EffectiveIntervalMs => IntervalMs > 0 ? IntervalMs : Math.Max(TimeoutMs, 1);

        public TimeSpan PollInterval => TimeSpan.FromMilliseconds(EffectiveIntervalMs);

        public TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);

        public TimeSpan PacketLossDuration => TimeSpan.FromSeconds(PacketLossDurationSeconds);

        public TimeSpan LossFlapWindow => TimeSpan.FromSeconds(LossFlapWindowSeconds);

        /// <summary>界面与告警展示用的端点文本。</summary>
        public string Endpoint => BuildEndpoint(Protocol, Target, Port);

        /// <summary>目标是否可作为主机名（IP 或合法域名）。</summary>
        public static bool IsValidHost(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return false;

            string value = target.Trim();
            return IPAddress.TryParse(value, out _) || Uri.CheckHostName(value) != UriHostNameType.Unknown;
        }

        /// <summary>目标是否是合法的 http / https 绝对地址。</summary>
        public static bool IsValidHttpUrl(string? target)
            => Uri.TryCreate(target?.Trim(), UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        public static string BuildEndpoint(ProbeProtocol protocol, string target, int port)
        {
            if (protocol == ProbeProtocol.Http)
                return target;

            return protocol.RequiresPort() && port > 0
                ? $"{protocol.Scheme()}://{target}:{port}"
                : $"{protocol.Scheme()}://{target}";
        }

        /// <param name="json">JSON 任务配置。</param>
        /// <param name="baseDirectory">
        /// udpPayload 相对路径的基准目录（通常为配置文件所在目录）。
        /// 为 null 时用当前工作目录。
        /// </param>
        public static ProbeTaskOptions FromJson(ProbeTaskConfigJson json, string? baseDirectory = null)
        {
            ProbeProtocolExtensions.TryParseProtocol(json.Protocol, out ProbeProtocol protocol);

            string udpPayload = string.IsNullOrEmpty(json.UdpPayload) ? UdpPayloadResolver.DefaultText : json.UdpPayload;

            // 解析失败的情况已在启动期校验阶段拦截；这里回退到默认值，保证任务本身仍可运行。
            byte[] udpPayloadBytes = UdpPayloadResolver.TryResolve(udpPayload, baseDirectory, out byte[] resolved, out _)
                ? resolved
                : Encoding.UTF8.GetBytes(UdpPayloadResolver.DefaultText);

            return new ProbeTaskOptions
            {
                Name = (json.Name ?? string.Empty).Trim(),
                Protocol = protocol,
                Target = (json.Target ?? string.Empty).Trim(),
                Port = json.Port ?? 0,
                TimeoutMs = json.Timeout,
                IntervalMs = json.Interval,
                SampleWindow = json.MaxRecentPackets,
                SignificantDelayThresholdMs = json.SignificantDelayThreshold,
                PacketLossDurationSeconds = json.PacketLossDuration,
                PacketLossCount = json.PacketLossCount,
                LossFlapWindowSeconds = json.LossFlapWindowSeconds,
                LossFlapCount = json.LossFlapCount,
                PayloadSize = json.PayloadSize ?? DefaultPayloadSize,
                UdpPayload = udpPayload,
                UdpPayloadBytes = udpPayloadBytes,
                HttpMethod = NormalizeHttpMethod(json.HttpMethod),
                ExpectedStatus = json.ExpectedStatus?.ToArray() ?? [],
                FollowRedirects = json.FollowRedirects,
                ReuseConnection = json.ReuseConnection,
                UseProxy = json.UseProxy
            };
        }

        public ProbeTaskConfigJson ToJson() => new()
        {
            Name = Name,
            Protocol = Protocol.ToConfigString(),
            Target = Target,
            Port = Port > 0 ? Port : null,
            Timeout = TimeoutMs,
            Interval = IntervalMs,
            MaxRecentPackets = SampleWindow,
            SignificantDelayThreshold = SignificantDelayThresholdMs,
            PacketLossDuration = PacketLossDurationSeconds,
            PacketLossCount = PacketLossCount,
            LossFlapWindowSeconds = LossFlapWindowSeconds,
            LossFlapCount = LossFlapCount,
            PayloadSize = Protocol == ProbeProtocol.Icmp ? PayloadSize : null,
            UdpPayload = Protocol == ProbeProtocol.Udp ? UdpPayload : null,
            HttpMethod = Protocol == ProbeProtocol.Http ? HttpMethod : null,
            ExpectedStatus = Protocol == ProbeProtocol.Http && ExpectedStatus.Count > 0 ? [.. ExpectedStatus] : null,
            FollowRedirects = FollowRedirects,
            ReuseConnection = ReuseConnection,
            UseProxy = UseProxy
        };

        /// <summary>把 HTTP 方法规范成 HEAD / GET；其他值退回 HEAD（校验阶段已拦截）。</summary>
        public static string NormalizeHttpMethod(string? method)
        {
            string value = method?.Trim().ToUpperInvariant() ?? string.Empty;
            return value == "GET" ? "GET" : "HEAD";
        }
    }
}
