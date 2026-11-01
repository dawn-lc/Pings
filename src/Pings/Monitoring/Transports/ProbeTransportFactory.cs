namespace Pings.Monitoring.Transports
{
    /// <summary>
    /// 按 <see cref="ProbeTaskOptions.Protocol" /> 创建对应的传输层实现。
    /// </summary>
    public static class ProbeTransportFactory
    {
        public static IProbeTransport Create(ProbeTaskOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return options.Protocol switch
            {
                ProbeProtocol.Icmp => new IcmpProbeTransport(options),
                ProbeProtocol.Tcp => new TcpProbeTransport(options),
                ProbeProtocol.Udp => new UdpProbeTransport(options),
                ProbeProtocol.Http => new HttpProbeTransport(options),
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.Protocol, "不支持的探测协议")
            };
        }
    }
}
