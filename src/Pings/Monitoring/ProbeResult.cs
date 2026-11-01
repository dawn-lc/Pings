namespace Pings.Monitoring
{
    /// <summary>
    /// 单次探测结果。
    /// </summary>
    /// <param name="Status">结果状态。</param>
    /// <param name="RttMs">成功时的端到端往返耗时（毫秒）；失败时为 null。</param>
    /// <param name="Detail">协议特有的原始信息（IPStatus 描述、SocketError 文本、HTTP 状态码），可为空。</param>
    /// <param name="ConnectMs">成功时建立连接的耗时（毫秒），仅 TCP / HTTP 有值。</param>
    public readonly record struct ProbeResult(
        ProbeStatus Status,
        double? RttMs = null,
        string? Detail = null,
        double? ConnectMs = null)
    {
        public static ProbeResult Success(double rttMs, string? detail = null, double? connectMs = null)
            => new(ProbeStatus.Success, rttMs, detail, connectMs);

        public static ProbeResult Failure(ProbeStatus status, string? detail = null)
            => new(status, null, detail);
    }
}
