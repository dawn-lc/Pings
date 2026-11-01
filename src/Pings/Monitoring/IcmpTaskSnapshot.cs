using System.Net.NetworkInformation;

namespace Pings.Monitoring
{
    /// <summary>
    /// 某个任务在某一时刻的只读快照，界面只依赖它渲染，不直接读任务内部状态。
    /// </summary>
    public sealed record IcmpTaskSnapshot(
        string Name,
        string Target,
        IPStatus Status,
        IcmpFaultCategory Category,
        double? DelayMs,
        double JitterMs,
        double WindowLossRate,
        double TotalLossRate,
        long Sent,
        long Received,
        DateTimeOffset Timestamp,
        string LastEvent,
        string? Warning,
        int WarningCount)
    {
        public bool IsSuccess => Status == IPStatus.Success;

        /// <summary>状态的中文描述。</summary>
        public string StatusText => Status.ToChineseString();

        /// <summary>延迟文本，丢包时显示 "-"。</summary>
        public string DelayText => DelayMs is { } delay ? $"{delay:0}ms" : "-";

        /// <summary>抖动文本，样本不足时显示 "-"。</summary>
        public string JitterText => JitterMs > 0 ? $"±{JitterMs:0.#}ms" : "-";

        /// <summary>滑窗丢包率文本。</summary>
        public string LossText => $"{WindowLossRate * 100:0.0}%";
    }
}
