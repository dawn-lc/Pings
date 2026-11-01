using System.Net.NetworkInformation;
using System.Net.Sockets;

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
                return ProbeResult.Failure(SocketErrorMapping.Map(socket.SocketErrorCode), socket.Message);
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

        internal static string Describe(IPStatus status) => status switch
        {
            IPStatus.BadDestination => "目标地址错误",
            IPStatus.BadHeader => "头部无效",
            IPStatus.BadOption => "选项无效",
            IPStatus.BadRoute => "路由无效",
            IPStatus.DestinationHostUnreachable => "目标主机不可达",
            IPStatus.DestinationNetworkUnreachable => "目标网络不可达",
            IPStatus.DestinationPortUnreachable => "目标端口不可达",
            IPStatus.DestinationProhibited => "目标禁止访问",
            IPStatus.DestinationScopeMismatch => "目标范围不匹配",
            IPStatus.DestinationUnreachable => "目标不可达",
            IPStatus.HardwareError => "硬件错误",
            IPStatus.IcmpError => "ICMP协议错误",
            IPStatus.NoResources => "网络资源不足",
            IPStatus.PacketTooBig => "数据包过大",
            IPStatus.ParameterProblem => "参数问题",
            IPStatus.SourceQuench => "数据包被放弃",
            IPStatus.Success => "通讯正常",
            IPStatus.TimedOut => "请求超时",
            IPStatus.TimeExceeded => "生存时间过期",
            IPStatus.TtlExpired => "TTL值过期",
            IPStatus.TtlReassemblyTimeExceeded => "重组超时",
            IPStatus.Unknown => "未知状态",
            IPStatus.UnrecognizedNextHeader => "下一标头无法识别",
            _ => "未知错误"
        };

        public ValueTask DisposeAsync()
        {
            _ping.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
