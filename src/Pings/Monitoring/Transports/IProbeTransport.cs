namespace Pings.Monitoring.Transports
{
    /// <summary>
    /// 一次探测的 I/O 抽象：每个协议实现一个，任务层不感知差异。
    /// </summary>
    /// <remarks>
    /// 约定：
    /// <list type="bullet">
    /// <item>「进程退出」必须用 <see cref="OperationCanceledException" /> 表达（令牌已取消）；</item>
    /// <item>协议层超时必须转成 <see cref="ProbeStatus.Timeout" /> 之类的状态，不能抛取消异常；</item>
    /// <item>其余异常由任务层兜底为 <see cref="ProbeStatus.LocalError" />，但实现仍应自行映射，以给出可读的 Detail。</item>
    /// </list>
    /// </remarks>
    public interface IProbeTransport : IAsyncDisposable
    {
        ValueTask<ProbeResult> ProbeAsync(CancellationToken token);
    }
}
