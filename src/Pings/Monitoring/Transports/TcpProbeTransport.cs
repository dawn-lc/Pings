using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Pings.Monitoring.Transports
{
    /// <summary>
    /// TCP 建连探测：只测「三次握手能否在超时内完成」，不发送任何业务数据。
    /// </summary>
    public sealed class TcpProbeTransport : IProbeTransport
    {
        private readonly ProbeTaskOptions _options;

        public TcpProbeTransport(ProbeTaskOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (options.Port is < 1 or > 65_535)
                throw new ArgumentException($"TCP 探测需要 1~65535 的端口，实际为 {options.Port}", nameof(options));
        }

        public async ValueTask<ProbeResult> ProbeAsync(CancellationToken token)
        {
            using Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            // 链接令牌：外部取消 → 抛取消异常退出循环；自身超时 → 记一次超时。
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_options.TimeoutMs);

            long started = Stopwatch.GetTimestamp();

            try
            {
                await socket.ConnectAsync(new DnsEndPoint(_options.Target, _options.Port), timeout.Token).ConfigureAwait(false);

                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                return ProbeResult.Success(elapsed, null, elapsed);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ProbeResult.Failure(ProbeStatus.Timeout, $"连接超时({_options.TimeoutMs}ms)");
            }
            catch (SocketException ex)
            {
                return ProbeResult.Failure(SocketErrorMapping.Map(ex.SocketErrorCode), SocketErrorMapping.Describe(ex.SocketErrorCode));
            }
            catch (Exception ex)
            {
                return ProbeResult.Failure(ProbeStatus.LocalError, ex.Message);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
