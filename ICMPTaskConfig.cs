using System.Net;

namespace Pings
{
    /// <summary>
    /// ICMP 任务配置结构，用于配置 ICMP 测试任务的参数
    /// </summary>
    public struct ICMPTaskConfig
    {
        private string name;
        private string ip;
        private int? timeout;
        private int? maxRecentPackets;
        private int? significantDelayThreshold;
        private double? packetLossDuration;
        private int? packetLossCount;

        /// <summary>任务名称</summary>
        public string Name
        {
            get => name ?? throw new ArgumentException("Invalid Name.");
            set
            {
                if (value.Length < 1 || value.Trim() == "")
                    throw new ArgumentException($"Invalid Name \"{value}\".");
                name = value;
            }
        }

        /// <summary>目标 IP 地址或域名</summary>
        public string IP
        {
            get => ip;
            set
            {
                if (!IsValidIP(value))
                    throw new ArgumentException($"Invalid IP address \"{value}\".");
                ip = value;
            }
        }

        /// <summary>Ping 超时时间（毫秒）</summary>
        public int Timeout
        {
            get => timeout ?? 1000;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be non-negative.");
                timeout = value;
            }
        }

        /// <summary>最大最近数据包记录数</summary>
        public int MaxRecentPackets
        {
            get => maxRecentPackets ?? 255;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(MaxRecentPackets), "MaxRecentPackets must be non-negative.");
                maxRecentPackets = value;
            }
        }

        /// <summary>显著延迟变化阈值（毫秒）</summary>
        public int SignificantDelayThreshold
        {
            get => significantDelayThreshold ?? 20;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(SignificantDelayThreshold), "SignificantDelayThreshold must be non-negative.");
                significantDelayThreshold = value;
            }
        }

        /// <summary>丢包持续时间阈值（秒）</summary>
        public TimeSpan PacketLossDuration
        {
            get => TimeSpan.FromSeconds(packetLossDuration ?? (Timeout / 1000) * 30);
            set
            {
                if (value.TotalMilliseconds < Timeout)
                    throw new ArgumentOutOfRangeException(nameof(PacketLossDuration), $"PacketLossDuration must be greater than {nameof(Timeout)}.");
                packetLossDuration = value.TotalSeconds;
            }
        }

        /// <summary>丢包计数阈值</summary>
        public int PacketLossCount
        {
            get => packetLossCount ?? 5;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(PacketLossCount), "PacketLossCount must be non-negative.");
                packetLossCount = value;
            }
        }

        /// <summary>
        /// 从具体参数构造配置（推荐用于JSON配置）
        /// </summary>
        public ICMPTaskConfig(string name, string ip, int timeout = 1000, int maxRecentPackets = 255, int significantDelayThreshold = 20, double packetLossDuration = 30.0, int packetLossCount = 5)
        {
            // 直接设置字段值，避免编译器警告
            this.name = name;
            if (!IsValidIP(ip)&& !IsValidDomainName(ip))
                throw new ArgumentException($"Invalid IP address or Domain \"{ip}\".");
            this.ip = ip;

            Timeout = timeout;
            MaxRecentPackets = maxRecentPackets;
            SignificantDelayThreshold = significantDelayThreshold;
            PacketLossDuration = TimeSpan.FromSeconds(packetLossDuration);
            PacketLossCount = packetLossCount;
        }

        /// <summary>验证 IP 地址是否有效</summary>
        private static bool IsValidIP(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }

        /// <summary>验证域名是否有效</summary>
        private static bool IsValidDomainName(string name)
        {
            return Uri.CheckHostName(name) != UriHostNameType.Unknown;
        }
    }
}