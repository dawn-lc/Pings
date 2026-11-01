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
                return await ProbeOnceAsync(_ping, _payload, token).ConfigureAwait(false);
            }
            catch (PlatformNotSupportedException ex)
            {
                // 没有 CAP_NET_RAW 的 Linux 上，.NET 会改用系统 ping 工具；那条路径禁止自定义负载
                // （只支持 -p 的 16 字节重复模式，且并非所有发行版都提供）。负载只是填充字节，
                // 能探测比负载形状重要，所以换一个干净的 Ping 用默认负载再试一次。
                // 若是「系统没有 ping 工具」，这次重试同样会失败，由下面的 catch 收敛成 LocalError。
                if (_payload.Length == 0)
                    return ProbeResult.Failure(ProbeStatus.LocalError, ex.Message);

                using Ping fallback = new();

                try
                {
                    return await ProbeOnceAsync(fallback, Array.Empty<byte>(), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception fallbackError)
                {
                    return ProbeResult.Failure(ProbeStatus.LocalError, fallbackError.Message);
                }
            }
        }

        /// <summary>用指定的 <see cref="Ping"/> 实例与负载探测一次；平台回退异常交给调用方决定。</summary>
        private async Task<ProbeResult> ProbeOnceAsync(Ping ping, byte[] payload, CancellationToken token)
        {
            try
            {
                // .NET 的 (string, CancellationToken) 组合只有 TimeSpan 超时版本，
                // int 超时版本没有 CancellationToken 形参。
                PingReply reply = await ping
                    .SendPingAsync(_options.Target, _options.Timeout, payload, null, token)
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
            catch (PlatformNotSupportedException)
            {
                // 只有「自定义负载」这一种情况值得换默认负载重试，由 ProbeAsync 判断。
                throw;
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
