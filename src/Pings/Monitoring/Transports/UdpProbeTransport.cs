using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Pings.Monitoring.Transports
{
    /// <summary>
    /// UDP 探测：发送一段负载并等待应用层回声。
    /// </summary>
    /// <remarks>
    /// UDP 没有握手，语义必须说清楚：
    /// <list type="bullet">
    /// <item>只有对端真的回了包才算成功，RTT 才是端到端往返；</item>
    /// <item>收到 ICMP 端口不可达时（connected UDP 会以
    ///   <c>ConnectionRefused</c> / <c>ConnectionReset</c> 暴露）判定为
    ///   <see cref="ProbeStatus.PortClosed" />：主机是通的，服务没在监听；</item>
    /// <item>其余情况没有回声只能算 <see cref="ProbeStatus.NoReply" />，
    ///   无法区分「主机不可达」与「服务不响应」。需要连通性判断时请改用 TCP 探测。</item>
    /// </list>
    /// </remarks>
    public sealed class UdpProbeTransport : IProbeTransport
    {
        private readonly ProbeTaskOptions _options;
        private readonly byte[] _payload;
        private readonly byte[] _buffer;

        public UdpProbeTransport(ProbeTaskOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (options.Port is < 1 or > 65_535)
                throw new ArgumentException($"UDP 探测需要 1~65535 的端口，实际为 {options.Port}", nameof(options));

            _payload = options.UdpPayloadBytes;
            _buffer = new byte[Math.Max(_payload.Length, 512)];
        }

        public async ValueTask<ProbeResult> ProbeAsync(CancellationToken token)
        {
            using Socket socket = new(SocketType.Dgram, ProtocolType.Udp);

            try
            {
                // Connect 不发送报文，只做 DNS 解析并绑定对端，之后才能收到 ICMP 错误。
                await socket.ConnectAsync(new DnsEndPoint(_options.Target, _options.Port), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (SocketException ex)
            {
                return MapSocketError(ex);
            }
            catch (Exception ex)
            {
                return ProbeResult.Failure(ProbeStatus.LocalError, ex.Message);
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_options.TimeoutMs);

            long started = Stopwatch.GetTimestamp();

            try
            {
                await socket.SendAsync(_payload, SocketFlags.None, timeout.Token).ConfigureAwait(false);

                int received = await socket.ReceiveAsync(_buffer, SocketFlags.None, timeout.Token).ConfigureAwait(false);
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                return received > 0
                    ? ProbeResult.Success(elapsed)
                    : ProbeResult.Failure(ProbeStatus.NoReply, "回声为空");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ProbeResult.Failure(ProbeStatus.NoReply, $"未收到回声({_options.TimeoutMs}ms)");
            }
            catch (SocketException ex)
            {
                return MapSocketError(ex);
            }
            catch (Exception ex)
            {
                return ProbeResult.Failure(ProbeStatus.LocalError, ex.Message);
            }
        }

        /// <summary>
        /// connected UDP 上收到 ICMP 端口不可达时表现为 <c>ConnectionReset</c> /
        /// <c>ConnectionRefused</c>：主机是通的，只是服务没在监听。
        /// </summary>
        private static ProbeResult MapSocketError(SocketException exception)
        {
            ProbeStatus status = SocketErrorMapping.Map(exception.SocketErrorCode);

            string detail = status == ProbeStatus.PortClosed
                ? "端口不可达"
                : SocketErrorMapping.Describe(exception.SocketErrorCode);

            return ProbeResult.Failure(status, detail);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
