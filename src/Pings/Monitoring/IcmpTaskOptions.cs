using System.Net;
using Pings.Configuration;

namespace Pings.Monitoring
{
    /// <summary>
    /// 单个监控任务的运行期参数（已校验、不可变）。
    /// </summary>
    public sealed class IcmpTaskOptions
    {
        public const int DefaultTimeoutMs = 1000;
        public const int DefaultSampleWindow = 255;
        public const int DefaultSignificantDelayThresholdMs = 20;
        public const double DefaultPacketLossDurationSeconds = 30;
        public const int DefaultPacketLossCount = 5;

        /// <summary>任务名称。</summary>
        public required string Name { get; init; }

        /// <summary>目标 IP 或域名。</summary>
        public required string Target { get; init; }

        /// <summary>单次 Ping 超时（毫秒）。</summary>
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

        /// <summary>实际探测间隔：未显式配置时与超时时间一致（保持旧版行为）。</summary>
        public int EffectiveIntervalMs => IntervalMs > 0 ? IntervalMs : Math.Max(TimeoutMs, 1);

        public TimeSpan PollInterval => TimeSpan.FromMilliseconds(EffectiveIntervalMs);

        public TimeSpan PacketLossDuration => TimeSpan.FromSeconds(PacketLossDurationSeconds);

        /// <summary>目标是否可作为 Ping 目标（IP 或合法主机名）。</summary>
        public static bool IsValidTarget(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return false;

            string value = target.Trim();
            return IPAddress.TryParse(value, out _) || Uri.CheckHostName(value) != UriHostNameType.Unknown;
        }

        public static IcmpTaskOptions FromJson(IcmpTaskConfigJson json) => new()
        {
            Name = (json.Name ?? string.Empty).Trim(),
            Target = (json.IP ?? string.Empty).Trim(),
            TimeoutMs = json.Timeout,
            IntervalMs = json.Interval,
            SampleWindow = json.MaxRecentPackets,
            SignificantDelayThresholdMs = json.SignificantDelayThreshold,
            PacketLossDurationSeconds = json.PacketLossDuration,
            PacketLossCount = json.PacketLossCount
        };

        public IcmpTaskConfigJson ToJson() => new()
        {
            Name = Name,
            IP = Target,
            Timeout = TimeoutMs,
            Interval = IntervalMs,
            MaxRecentPackets = SampleWindow,
            SignificantDelayThreshold = SignificantDelayThresholdMs,
            PacketLossDuration = PacketLossDurationSeconds,
            PacketLossCount = PacketLossCount
        };
    }
}
