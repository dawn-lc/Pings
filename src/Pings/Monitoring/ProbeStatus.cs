namespace Pings.Monitoring
{
    /// <summary>
    /// 单次探测的结果状态，与具体协议无关。
    /// </summary>
    public enum ProbeStatus
    {
        /// <summary>尚未探测。</summary>
        Unknown,
        /// <summary>探测成功。</summary>
        Success,
        /// <summary>没有任何回应（ICMP 超时 / TCP 建连超时 / HTTP 建连超时）。</summary>
        Timeout,
        /// <summary>报文已发出但没有应用层回应（UDP 未收到回声）。</summary>
        NoReply,
        /// <summary>收到明确的失败回应（ICMP 不可达 / 禁止访问 / TTL 过期）。</summary>
        Unreachable,
        /// <summary>主机可达但服务端口关闭（TCP RST / UDP 端口不可达）。</summary>
        PortClosed,
        /// <summary>域名解析失败。</summary>
        DnsFailure,
        /// <summary>TLS 握手失败。</summary>
        TlsFailure,
        /// <summary>HTTP 状态码不在期望集合内。</summary>
        HttpStatusUnexpected,
        /// <summary>连接已建立但响应阶段超时。</summary>
        ResponseTimeout,
        /// <summary>报文格式 / 协议错误。</summary>
        ProtocolError,
        /// <summary>本机权限或套接字资源问题。</summary>
        LocalError
    }

    /// <summary>
    /// 故障类别，用于事件聚合与告警去重。
    /// </summary>
    public enum ProbeFaultCategory
    {
        /// <summary>正常。</summary>
        None,
        /// <summary>无响应（超时 / 无回声 / 响应超时）。</summary>
        NoResponse,
        /// <summary>目标不可达。</summary>
        Unreachable,
        /// <summary>主机可达但服务不可用（端口关闭 / 状态码异常 / TLS 失败）。</summary>
        ServiceUnavailable,
        /// <summary>域名解析问题。</summary>
        NameResolution,
        /// <summary>报文格式 / 协议问题。</summary>
        ProtocolError,
        /// <summary>本机资源或权限问题。</summary>
        LocalResource,
        /// <summary>未知。</summary>
        Unknown
    }

    /// <summary>
    /// 事件严重程度，数值越大越重要。
    /// </summary>
    public enum ProbeSeverity
    {
        /// <summary>正常 / 无事件。</summary>
        None = 0,
        /// <summary>提示性事件（延迟波动）。</summary>
        Info = 1,
        /// <summary>需要关注（状态变化、疑似中断、抖动链路）。</summary>
        Warning = 2,
        /// <summary>需要立即处理（中断确认、端口关闭、状态码异常）。</summary>
        Critical = 3
    }

    /// <summary>
    /// <see cref="ProbeStatus" /> 的派生事实，避免调用方各自 switch。
    /// </summary>
    public static class ProbeStatusFacts
    {
        public static bool IsSuccess(this ProbeStatus status) => status == ProbeStatus.Success;

        /// <summary>
        /// 是否计入「丢包」。只统计完全没有任何回应的情况；
        /// 端口关闭、DNS 失败、TLS 失败属于「主机可达或探测未发出」，不计入丢包率。
        /// </summary>
        public static bool CountsAsLoss(this ProbeStatus status)
            => status is ProbeStatus.Timeout or ProbeStatus.NoReply;

        /// <summary>所有非成功都算一次故障，但未必算丢包。</summary>
        public static bool IsFailure(this ProbeStatus status)
            => status is not (ProbeStatus.Success or ProbeStatus.Unknown);

        /// <summary>
        /// 是否计入中断 episode（用于告警升级）。比 <see cref="CountsAsLoss" /> 更宽：
        /// 明确不可达与响应超时同样说明目标不可用。
        /// </summary>
        public static bool CountsAsOutage(this ProbeStatus status)
            => status is ProbeStatus.Timeout or ProbeStatus.NoReply or ProbeStatus.ResponseTimeout or ProbeStatus.Unreachable;

        /// <summary>
        /// 是否说明主机已经给出了回应（可以结束中断 episode）。
        /// 端口关闭 / TLS 失败 / 状态码异常都证明网络路径是通的。
        /// </summary>
        public static bool EndsOutage(this ProbeStatus status)
            => status is ProbeStatus.Success or ProbeStatus.PortClosed or ProbeStatus.TlsFailure or ProbeStatus.HttpStatusUnexpected;

        public static ProbeFaultCategory Category(this ProbeStatus status) => status switch
        {
            ProbeStatus.Success => ProbeFaultCategory.None,
            ProbeStatus.Timeout or ProbeStatus.NoReply or ProbeStatus.ResponseTimeout => ProbeFaultCategory.NoResponse,
            ProbeStatus.Unreachable => ProbeFaultCategory.Unreachable,
            ProbeStatus.PortClosed or ProbeStatus.HttpStatusUnexpected or ProbeStatus.TlsFailure => ProbeFaultCategory.ServiceUnavailable,
            ProbeStatus.DnsFailure => ProbeFaultCategory.NameResolution,
            ProbeStatus.ProtocolError => ProbeFaultCategory.ProtocolError,
            ProbeStatus.LocalError => ProbeFaultCategory.LocalResource,
            _ => ProbeFaultCategory.Unknown
        };

        /// <summary>
        /// 故障首次出现时的严重程度。中断升级由状态机另行提升到 Critical。
        /// </summary>
        public static ProbeSeverity Severity(this ProbeStatus status) => status switch
        {
            ProbeStatus.Success or ProbeStatus.Unknown => ProbeSeverity.None,
            ProbeStatus.PortClosed or ProbeStatus.HttpStatusUnexpected => ProbeSeverity.Critical,
            ProbeStatus.LocalError or ProbeStatus.ProtocolError => ProbeSeverity.Info,
            _ => ProbeSeverity.Warning
        };

        public static string ToChineseString(this ProbeStatus status) => status switch
        {
            ProbeStatus.Unknown => "未知状态",
            ProbeStatus.Success => "通讯正常",
            ProbeStatus.Timeout => "请求超时",
            ProbeStatus.NoReply => "无回应",
            ProbeStatus.Unreachable => "目标不可达",
            ProbeStatus.PortClosed => "端口关闭",
            ProbeStatus.DnsFailure => "域名解析失败",
            ProbeStatus.TlsFailure => "TLS握手失败",
            ProbeStatus.HttpStatusUnexpected => "状态码异常",
            ProbeStatus.ResponseTimeout => "响应超时",
            ProbeStatus.ProtocolError => "协议错误",
            ProbeStatus.LocalError => "本地网络错误",
            _ => "未知错误"
        };
    }
}
