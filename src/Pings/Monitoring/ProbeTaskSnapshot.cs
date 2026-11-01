namespace Pings.Monitoring
{
    /// <summary>
    /// 某个任务在某一时刻的只读快照，界面与通知只依赖它渲染。
    /// </summary>
    public sealed record ProbeTaskSnapshot(
        string Name,
        ProbeProtocol Protocol,
        string Target,
        string Endpoint,
        ProbeStatus Status,
        string? Detail,
        ProbeFaultCategory Category,
        ProbeSeverity Severity,
        double? DelayMs,
        double? ConnectMs,
        double EpisodeSeconds,
        ProbeStatisticsSnapshot Statistics,
        DateTimeOffset Timestamp,
        string LastEvent,
        string? Warning,
        int WarningCount,
        long DroppedWarnings)
    {
        public bool IsSuccess => Status == ProbeStatus.Success;

        /// <summary>状态的可读描述，优先使用协议原始信息（HTTP 状态码、IPStatus 具体原因）。</summary>
        public string StatusText => string.IsNullOrWhiteSpace(Detail) ? Status.ToDisplayString() : Detail;

        /// <summary>延迟文本，非成功时显示 "-"。</summary>
        public string DelayText => DelayMs is { } delay ? $"{delay:0}ms" : "-";

        /// <summary>抖动文本，样本不足时显示 "-"。</summary>
        public string JitterText => Statistics.JitterMs is { } jitter ? $"±{jitter:0.#}ms" : "-";

        /// <summary>滑窗丢包率文本。</summary>
        public string LossText => $"{Statistics.WindowLossRate * 100:0.0}%";

        /// <summary>滑窗故障率文本（含端口关闭、DNS / TLS 失败、状态码异常）。</summary>
        public string FailureText => $"{Statistics.WindowFailureRate * 100:0.0}%";
    }
}
