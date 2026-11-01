using System.Diagnostics;
using Pings.Monitoring.Transports;

namespace Pings.Monitoring
{
    /// <summary>
    /// 任务事件参数：携带只读快照与本次事件，订阅方无需回读任务内部状态。
    /// </summary>
    public sealed class ProbeTaskEventArgs(ProbeTaskSnapshot snapshot, ProbeEvent probeEvent) : EventArgs
    {
        public ProbeTaskSnapshot Snapshot { get; } = snapshot;

        public ProbeEvent Event { get; } = probeEvent;
    }

    /// <summary>
    /// 单个目标的探测任务：探测循环 + 状态机 + 统计，不接触任何界面对象。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>协议差异全部收敛到 <see cref="IProbeTransport" />，任务本身与协议无关；</item>
    /// <item>事件只描述「发生了什么」，界面通过 <see cref="Snapshot" /> 在刷新时统一渲染；</item>
    /// <item>构造函数不做任何 I/O，探测循环由 <see cref="Start" /> 显式启动。</item>
    /// </list>
    /// </remarks>
    public sealed class ProbeTask : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly IProbeTransport _transport;
        private readonly ProbeStatistics _statistics;
        private readonly ProbeStateMachine _state;

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private string? _detail;
        private double? _delayMs;
        private double? _connectMs;

        public ProbeTask(ProbeTaskOptions options, IProbeTransport transport, TimeProvider? time = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _statistics = new ProbeStatistics(options.SampleWindow);
            _state = new ProbeStateMachine(options, time);
            Warnings = new WarningQueue();
        }

        public ProbeTaskOptions Options { get; }

        public WarningQueue Warnings { get; }

        /// <summary>任何需要外部知晓的事件都通过这里发出（状态变化 / 恢复 / 延迟波动 / 告警）。</summary>
        public event EventHandler<ProbeTaskEventArgs>? EventRaised;

        /// <summary>启动探测循环（幂等）。</summary>
        public void Start(CancellationToken token = default)
        {
            lock (_gate)
            {
                if (_loop is not null)
                    return;

                _cts = token.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(token)
                    : new CancellationTokenSource();

                CancellationToken loopToken = _cts.Token;
                _loop = Task.Run(() => ProbeLoopAsync(loopToken), CancellationToken.None);
            }
        }

        /// <summary>执行一次探测（用于 --once 模式），不会启动循环。</summary>
        public async Task ProbeOnceAsync(CancellationToken token = default)
        {
            ProbeResult result = await SafeProbeAsync(token).ConfigureAwait(false);
            ApplyResult(result);
        }

        public ProbeTaskSnapshot Snapshot()
        {
            lock (_gate)
                return BuildSnapshot();
        }

        private async Task ProbeLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                long started = Stopwatch.GetTimestamp();

                try
                {
                    ProbeResult result = await SafeProbeAsync(token).ConfigureAwait(false);
                    ApplyResult(result);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
                TimeSpan remaining = Options.PollInterval - elapsed;

                if (remaining <= TimeSpan.Zero)
                    continue;

                try
                {
                    await Task.Delay(remaining, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 传输层只允许用取消异常表示「进程退出」；其余异常一律降级成一次本地故障，
        /// 否则单个协议的实现疏漏会直接终止探测循环。
        /// </summary>
        private async ValueTask<ProbeResult> SafeProbeAsync(CancellationToken token)
        {
            try
            {
                return await _transport.ProbeAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ProbeResult.Failure(ProbeStatus.Timeout, "探测被取消");
            }
            catch (Exception ex)
            {
                return ProbeResult.Failure(ProbeStatus.LocalError, ex.Message);
            }
        }

        private void ApplyResult(ProbeResult result)
        {
            ProbeTaskSnapshot snapshot;
            ProbeEvent[] events;

            lock (_gate)
            {
                _statistics.Record(result.Status, result.RttMs ?? 0);
                events = _state.Apply(result);

                _detail = result.Detail;
                _delayMs = result.Status.IsSuccess() ? result.RttMs : null;
                _connectMs = result.Status.IsSuccess() ? result.ConnectMs : null;

                snapshot = BuildSnapshot();
            }

            foreach (ProbeEvent probeEvent in events)
                Raise(new ProbeTaskEventArgs(snapshot, probeEvent));
        }

        /// <summary>
        /// 逐个调用订阅者并隔离异常：任何一个订阅方出问题都不能终止探测循环。
        /// </summary>
        private void Raise(ProbeTaskEventArgs args)
        {
            EventHandler<ProbeTaskEventArgs>? handler = EventRaised;
            if (handler is null)
                return;

            foreach (Delegate callback in handler.GetInvocationList())
            {
                if (callback is not EventHandler<ProbeTaskEventArgs> subscriber)
                    continue;

                try
                {
                    subscriber(this, args);
                }
                catch (Exception)
                {
                    // 订阅方异常不得影响探测；日志由订阅方自己负责。
                }
            }
        }

        private ProbeTaskSnapshot BuildSnapshot() => new(
            Name: Options.Name,
            Protocol: Options.Protocol,
            Target: Options.Target,
            Endpoint: Options.Endpoint,
            Status: _state.Status,
            Detail: _detail,
            Category: _state.Category,
            Severity: _state.Severity,
            DelayMs: _delayMs,
            ConnectMs: _connectMs,
            EpisodeSeconds: _state.EpisodeSeconds,
            Statistics: _statistics.Snapshot(),
            Timestamp: _state.LastEventAt,
            LastEvent: _state.LastEvent,
            Warning: Warnings.Peek(),
            WarningCount: Warnings.Count,
            DroppedWarnings: Warnings.Dropped);

        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            Task? loop;

            lock (_gate)
            {
                cts = _cts;
                loop = _loop;
                _cts = null;
                _loop = null;
            }

            if (cts is null)
                return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (loop is not null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cts.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            Warnings.Clear();
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
