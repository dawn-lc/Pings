namespace Pings.Monitoring
{
    /// <summary>
    /// 探测协议。
    /// </summary>
    public enum ProbeProtocol
    {
        /// <summary>ICMP 回显（默认）。</summary>
        Icmp,
        /// <summary>TCP 建连。</summary>
        Tcp,
        /// <summary>UDP 发送 + 回声。</summary>
        Udp,
        /// <summary>HTTP / HTTPS 请求。</summary>
        Http
    }

    /// <summary>
    /// <see cref="ProbeProtocol" /> 的解析与文本约定。
    /// </summary>
    public static class ProbeProtocolExtensions
    {
        /// <summary>是否需要在配置里显式给出端口。</summary>
        public static bool RequiresPort(this ProbeProtocol protocol)
            => protocol is ProbeProtocol.Tcp or ProbeProtocol.Udp;

        /// <summary>Endpoint 展示用的 scheme。</summary>
        public static string Scheme(this ProbeProtocol protocol) => protocol switch
        {
            ProbeProtocol.Icmp => "icmp",
            ProbeProtocol.Tcp => "tcp",
            ProbeProtocol.Udp => "udp",
            ProbeProtocol.Http => "http",
            _ => "icmp"
        };

        /// <summary>配置文件里的写法。</summary>
        public static string ToConfigString(this ProbeProtocol protocol)
            => protocol.Scheme();

        /// <summary>
        /// 解析配置里的协议名。留空按 ICMP 处理（返回 true，<paramref name="protocol" /> 为 <see cref="ProbeProtocol.Icmp" />）；
        /// 只有明确写了未知协议才返回 false。
        /// </summary>
        public static bool TryParseProtocol(string? value, out ProbeProtocol protocol)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                protocol = ProbeProtocol.Icmp;
                return true;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "icmp":
                    protocol = ProbeProtocol.Icmp;
                    return true;
                case "tcp":
                    protocol = ProbeProtocol.Tcp;
                    return true;
                case "udp":
                    protocol = ProbeProtocol.Udp;
                    return true;
                case "http":
                case "https":
                    protocol = ProbeProtocol.Http;
                    return true;
                default:
                    protocol = ProbeProtocol.Icmp;
                    return false;
            }
        }
    }
}
