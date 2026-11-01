using System.Net.Sockets;

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

        /// <summary>短中文描述，用于状态列。系统异常文本往往又长又是整句，窄窗口下会被截断。</summary>
        public static string Describe(SocketError error) => error switch
        {
            SocketError.TimedOut => "连接超时",
            SocketError.ConnectionRefused => "连接被拒绝",
            SocketError.ConnectionReset => "连接被重置",
            SocketError.ConnectionAborted => "连接被中止",
            SocketError.HostNotFound => "域名不存在",
            SocketError.NoData => "域名无解析结果",
            SocketError.NetworkUnreachable => "网络不可达",
            SocketError.HostUnreachable => "主机不可达",
            SocketError.NetworkDown => "网络已断开",
            SocketError.HostDown => "主机已关闭",
            SocketError.AccessDenied => "访问被拒绝",
            SocketError.TooManyOpenSockets => "套接字资源不足",
            SocketError.NoBufferSpaceAvailable => "缓冲区不足",
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
