using System.Net.Sockets;
using Pings.Properties;

namespace Pings.Monitoring.Transports
{
    /// <summary>
    /// <see cref="SocketError" /> → <see cref="ProbeStatus" /> 的统一映射，TCP / UDP / HTTP 共用。
    /// </summary>
    internal static class SocketErrorMapping
    {
        public static ProbeStatus Map(SocketError error) => error switch
        {
            SocketError.TimedOut => ProbeStatus.Timeout,

            // RST：主机是通的，只是服务没在监听。
            SocketError.ConnectionRefused
                or SocketError.ConnectionReset
                or SocketError.ConnectionAborted => ProbeStatus.PortClosed,

            SocketError.HostNotFound
                or SocketError.NoData => ProbeStatus.DnsFailure,

            SocketError.NetworkUnreachable
                or SocketError.HostUnreachable
                or SocketError.NetworkDown
                or SocketError.HostDown => ProbeStatus.Unreachable,

            _ => ProbeStatus.LocalError
        };

        /// <summary>短描述，用于状态列。系统异常文本往往又长又是整句，窄窗口下会被截断。</summary>
        /// <remarks>文案里不能有空格，原因见 <see cref="ProbeStatusFacts.ToDisplayString" />。</remarks>
        public static string Describe(SocketError error) => error switch
        {
            SocketError.TimedOut => Resources.SocketTimedOut,
            SocketError.ConnectionRefused => Resources.SocketConnectionRefused,
            SocketError.ConnectionReset => Resources.SocketConnectionReset,
            SocketError.ConnectionAborted => Resources.SocketConnectionAborted,
            SocketError.HostNotFound => Resources.SocketHostNotFound,
            SocketError.NoData => Resources.SocketNoData,
            SocketError.NetworkUnreachable => Resources.SocketNetworkUnreachable,
            SocketError.HostUnreachable => Resources.SocketHostUnreachable,
            SocketError.NetworkDown => Resources.SocketNetworkDown,
            SocketError.HostDown => Resources.SocketHostDown,
            SocketError.AccessDenied => Resources.SocketAccessDenied,
            SocketError.TooManyOpenSockets => Resources.SocketTooManyOpenSockets,
            SocketError.NoBufferSpaceAvailable => Resources.SocketNoBufferSpaceAvailable,

            // 未收录的错误直接给枚举名：英文但至少可检索，比空串或整句系统文本更适合窄列。
            _ => error.ToString()
        };

        /// <summary>
        /// 从异常链里找出第一个 <see cref="SocketException" />。
        /// <see cref="System.Net.Http.HttpRequestException" /> 会把真实的套接字错误包在多层内部异常里。
        /// </summary>
        public static SocketException? Find(Exception? exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is SocketException socket)
                    return socket;
            }

            return null;
        }
    }
}
