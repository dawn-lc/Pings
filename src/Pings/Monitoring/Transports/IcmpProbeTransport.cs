using System.Net.NetworkInformation;
using System.Net.Sockets;
using Pings.Properties;

namespace Pings.Monitoring.Transports
{
    /// <summary>
    /// ICMP 回显探测。
    /// </summary>
    public sealed class IcmpProbeTransport : IProbeTransport
    {
        private readonly ProbeTaskOptions _options;
        private readonly byte[] _payload;
        private readonly Ping _ping = new();

        public IcmpProbeTransport(ProbeTaskOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _payload = new byte[Math.Clamp(options.PayloadSize, 0, 65_500)];
        }

        public async ValueTask<ProbeResult> ProbeAsync(CancellationToken token)
        {
            try
            {
                // .NET 的 (string, CancellationToken) 组合只有 TimeSpan 超时版本，
                // int 超时版本没有 CancellationToken 形参。
                PingReply reply = await _ping
                    .SendPingAsync(_options.Target, _options.Timeout, _payload, null, token)
                    .ConfigureAwait(false);

                return reply.Status == IPStatus.Success
                    ? ProbeResult.Success(reply.RoundtripTime)
                    : ProbeResult.Failure(MapStatus(reply.Status), Describe(reply.Status));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ProbeResult.Failure(ProbeStatus.Timeout, Describe(IPStatus.TimedOut));
            }
            catch (PingException ex) when (ex.InnerException is SocketException socket)
            {
                return ProbeResult.Failure(SocketErrorMapping.Map(socket.SocketErrorCode), SocketErrorMapping.Describe(socket.SocketErrorCode));
            }
            catch (Exception ex)
            {
                // 权限不足（Linux 非 root）、套接字资源不足等：算本机问题，但不能让循环退出。
                return ProbeResult.Failure(ProbeStatus.LocalError, ex.Message);
            }
        }

        internal static ProbeStatus MapStatus(IPStatus status) => status switch
        {
            IPStatus.Success => ProbeStatus.Success,
            IPStatus.TimedOut => ProbeStatus.Timeout,

            IPStatus.DestinationHostUnreachable
                or IPStatus.DestinationNetworkUnreachable
                or IPStatus.DestinationUnreachable
                or IPStatus.DestinationProhibited
                or IPStatus.DestinationPortUnreachable
                or IPStatus.DestinationScopeMismatch
                or IPStatus.BadDestination
                or IPStatus.BadRoute
                or IPStatus.TtlExpired
                or IPStatus.TimeExceeded
                or IPStatus.TtlReassemblyTimeExceeded => ProbeStatus.Unreachable,

            IPStatus.BadHeader
                or IPStatus.BadOption
                or IPStatus.ParameterProblem
                or IPStatus.UnrecognizedNextHeader
                or IPStatus.IcmpError
                or IPStatus.PacketTooBig => ProbeStatus.ProtocolError,

            IPStatus.NoResources
                or IPStatus.SourceQuench
                or IPStatus.HardwareError => ProbeStatus.LocalError,

            _ => ProbeStatus.Unknown
        };

        /// <summary>IPStatus 的短描述，用于状态列。</summary>
        /// <remarks>文案里不能有空格，原因见 <see cref="ProbeStatusFacts.ToDisplayString" />。</remarks>
        internal static string Describe(IPStatus status) => status switch
        {
            IPStatus.BadDestination => Resources.IpStatusBadDestination,
            IPStatus.BadHeader => Resources.IpStatusBadHeader,
            IPStatus.BadOption => Resources.IpStatusBadOption,
            IPStatus.BadRoute => Resources.IpStatusBadRoute,
            IPStatus.DestinationHostUnreachable => Resources.IpStatusDestinationHostUnreachable,
            IPStatus.DestinationNetworkUnreachable => Resources.IpStatusDestinationNetworkUnreachable,
            IPStatus.DestinationPortUnreachable => Resources.IpStatusDestinationPortUnreachable,
            IPStatus.DestinationProhibited => Resources.IpStatusDestinationProhibited,
            IPStatus.DestinationScopeMismatch => Resources.IpStatusDestinationScopeMismatch,
            IPStatus.DestinationUnreachable => Resources.IpStatusDestinationUnreachable,
            IPStatus.HardwareError => Resources.IpStatusHardwareError,
            IPStatus.IcmpError => Resources.IpStatusIcmpError,
            IPStatus.NoResources => Resources.IpStatusNoResources,
            IPStatus.PacketTooBig => Resources.IpStatusPacketTooBig,
            IPStatus.ParameterProblem => Resources.IpStatusParameterProblem,
            IPStatus.SourceQuench => Resources.IpStatusSourceQuench,
            IPStatus.Success => Resources.IpStatusSuccess,
            IPStatus.TimedOut => Resources.IpStatusTimedOut,
            IPStatus.TimeExceeded => Resources.IpStatusTimeExceeded,
            IPStatus.TtlExpired => Resources.IpStatusTtlExpired,
            IPStatus.TtlReassemblyTimeExceeded => Resources.IpStatusTtlReassemblyTimeExceeded,
            IPStatus.Unknown => Resources.IpStatusUnknown,
            IPStatus.UnrecognizedNextHeader => Resources.IpStatusUnrecognizedNextHeader,
            _ => Resources.IpStatusFallback
        };

        public ValueTask DisposeAsync()
        {
            _ping.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
