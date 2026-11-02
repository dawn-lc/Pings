using Pings.Properties;

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
    /// 故障类别。它同时是**事件聚合与告警去重的粒度**：同一类别内的不同表现（<c>请求超时 ↔ 目标主机不可达</c>、
    /// <c>超时 ↔ 无回应</c>）不重复产生状态事件。
    /// </summary>
    /// <remarks>
    /// 为何 <c>Unreachable</c> 不单独成类：设备离线时，ARP 缓存里还有旧条目就表现为超时，
    /// 条目过期后重新解析失败就表现为不可达——两者随 ARP 状态每几秒翻转一次，是同一个故障的两个观测面。
    /// 若单独成类，一台离线设备会把日志刷满，真正的状态变化与中断告警反被淹没。
    /// 具体原因不会丢：<see cref="ProbeStatus" /> 本身、界面状态列与日志文案仍然区分两者。
    /// </remarks>
    public enum ProbeFaultCategory
    {
        /// <summary>正常。</summary>
        None,
        /// <summary>目标没有给出可用回应：超时、无回声、响应超时、明确不可达（ICMP 不可达 / TTL 过期）。</summary>
        NoResponse,
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
            ProbeStatus.Timeout or ProbeStatus.NoReply or ProbeStatus.ResponseTimeout or ProbeStatus.Unreachable => ProbeFaultCategory.NoResponse,
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

        /// <summary>
        /// 面向界面的可读文案，随界面语言变化。
        /// </summary>
        /// <remarks>
        /// **不要**用它做逻辑判断，也不要写进日志后再解析回来 —— 换语言后文本就变了。
        /// 需要稳定的判据请用 <see cref="Category" /> / <see cref="Severity" /> 或枚举本身。
        ///
        /// 文案里**不能有空格**：实时表格的「状态」列靠 <c>Overflow.Ellipsis</c> 截断，
        /// 而折行只会发生在空格处 —— 一旦出现空格，行高会被撑开，整个活动区域布局错乱。
        /// </remarks>
        public static string ToDisplayString(this ProbeStatus status) => status switch
        {
            ProbeStatus.Unknown => Resources.StatusUnknown,
            ProbeStatus.Success => Resources.StatusSuccess,
            ProbeStatus.Timeout => Resources.StatusTimeout,
            ProbeStatus.NoReply => Resources.StatusNoReply,
            ProbeStatus.Unreachable => Resources.StatusUnreachable,
            ProbeStatus.PortClosed => Resources.StatusPortClosed,
            ProbeStatus.DnsFailure => Resources.StatusDnsFailure,
            ProbeStatus.TlsFailure => Resources.StatusTlsFailure,
            ProbeStatus.HttpStatusUnexpected => Resources.StatusHttpStatusUnexpected,
            ProbeStatus.ResponseTimeout => Resources.StatusResponseTimeout,
            ProbeStatus.ProtocolError => Resources.StatusProtocolError,
            ProbeStatus.LocalError => Resources.StatusLocalError,
            _ => Resources.StatusFallback
        };
    }
}
