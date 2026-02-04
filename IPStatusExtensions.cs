using System.Net.NetworkInformation;

namespace Pings
{
    /// <summary>
    /// ICMP故障分类
    /// </summary>
    public enum IcmpFaultCategory
    {
        /// <summary>正常</summary>
        None,
        /// <summary>连通性丢失（核心告警）</summary>
        ConnectivityLoss,
        /// <summary>路由/寻址问题</summary>
        RoutingError,
        /// <summary>TTL / 生命周期问题</summary>
        PacketLifetimeExceeded,
        /// <summary>报文格式/协议问题</summary>
        PacketFormatError,
        /// <summary>本地或网络资源不足</summary>
        ResourceExhausted,
        /// <summary>被策略/ACL/防火墙拒绝</summary>
        AccessDenied,
        /// <summary>硬件错误</summary>
        HardwareFailure,
        /// <summary>未知</summary>
        Unknown
    }

    /// <summary>
    /// IPStatus 枚举的扩展方法，提供中文描述
    /// </summary>
    public static class IPStatusExtensions
    {
        /// <summary>
        /// 将 IPStatus 分类为 IcmpFaultCategory
        /// </summary>
        public static IcmpFaultCategory Classify(this IPStatus status) => status
            switch
        {
            // ✅ 正常
            IPStatus.Success
                => IcmpFaultCategory.None,

            // ❌ 连通性丢失（视为同一问题）
            IPStatus.TimedOut
            or IPStatus.DestinationHostUnreachable
            or IPStatus.DestinationNetworkUnreachable
            or IPStatus.DestinationUnreachable
                => IcmpFaultCategory.ConnectivityLoss,

            // 🚫 访问被拒绝 / 策略问题
            IPStatus.DestinationProhibited
            or IPStatus.DestinationPortUnreachable
            or IPStatus.DestinationScopeMismatch
                => IcmpFaultCategory.AccessDenied,

            // 🧭 路由 / 寻址问题
            IPStatus.BadDestination
            or IPStatus.BadRoute
                => IcmpFaultCategory.RoutingError,

            // ⏱ 生命周期 / TTL
            IPStatus.TtlExpired
            or IPStatus.TimeExceeded
            or IPStatus.TtlReassemblyTimeExceeded
                => IcmpFaultCategory.PacketLifetimeExceeded,

            // 📦 报文 / 协议问题
            IPStatus.BadHeader
            or IPStatus.BadOption
            or IPStatus.ParameterProblem
            or IPStatus.UnrecognizedNextHeader
            or IPStatus.IcmpError
                => IcmpFaultCategory.PacketFormatError,

            // 💾 资源耗尽
            IPStatus.NoResources
            or IPStatus.SourceQuench
                => IcmpFaultCategory.ResourceExhausted,

            // 🔧 硬件
            IPStatus.HardwareError
                => IcmpFaultCategory.HardwareFailure,

            // ❓ 未知
            IPStatus.Unknown
            or _
                => IcmpFaultCategory.Unknown
        };

        /// <summary>
        /// 将 IPStatus 转换为中文描述
        /// </summary>
        public static string ToChineseString(this IPStatus status)
        {
            return status switch
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
                _ => "未知错误",
            };
        }
    }
}