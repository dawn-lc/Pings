using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Pings.Monitoring
{
    /// <summary>
    /// 任务事件参数：携带只读快照与上一次的故障类别，订阅方无需再读任务内部状态。
    /// </summary>
    public sealed class IcmpTaskEventArgs(IcmpTaskSnapshot snapshot, IcmpFaultCategory previousCategory) : EventArgs
    {
        public IcmpTaskSnapshot Snapshot { get; } = snapshot;

        public IcmpFaultCategory PreviousCategory { get; } = previousCategory;
    }

    /// <summary>
    /// 单个目标的 ICMP 探测任务。
    /// </summary>
    /// <remarks>
    /// 设计要点（对应参考项目的「动作与渲染正交化」）：
    /// <list type="bullet">
    /// <item>任务只维护状态与统计，不接触任何界面对象；</item>
    /// <item>事件只用于「发生了什么」，界面通过 <see cref="Snapshot"/> 在刷新时统一渲染；</item>
    /// <item>构造函数不做任何 I/O，探测循环由 <see cref="Start"/> 显式启动。</item>
    /// </list>
    /// </remarks>
    public sealed class IcmpTestTask : IAsyncDisposable
    {
        private static readonly byte[] Payload = new byte[32];

        private readonly object _gate = new();
        private readonly IcmpStatistics _statistics;

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private IPStatus _status = IPStatus.Unknown;
        private IcmpFaultCategory _category = IcmpFaultCategory.Unknown;
        private double? _lastDelayMs;
        private string _lastEvent = "暂无事件";
        private DateTimeOffset _lastEventAt = DateTimeOffset.Now;
        private DateTimeOffset? _lossEpisodeStart;
        private int _consecutiveLosses;
        private bool _lossAlarmRaised;

        public IcmpTestTask(IcmpTaskOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _statistics = new IcmpStatistics(options.SampleWindow);
            Warnings = new WarningQueue();
        }

        public IcmpTaskOptions Options { get; }

        public WarningQueue Warnings { get; }

        /// <summary>状态发生变化（含恢复）时触发。</summary>
        public event EventHandler<IcmpTaskEventArgs>? StatusChanged;

        /// <summary>延迟相对上一次成功探测显著升高时触发。</summary>
        public event EventHandler<IcmpTaskEventArgs>? DelaySpiked;

        /// <summary>连续丢包达到阈值时触发（每次丢包episode只触发一次）。</summary>
        public event EventHandler<IcmpTaskEventArgs>? LossAlarmRaised;

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
            using Ping ping = new();
            await ProbeAsync(ping, token).ConfigureAwait(false);
        }

        public IcmpTaskSnapshot Snapshot()
        {
            lock (_gate)
            {
                IcmpStatisticsSnapshot stats = _statistics.Snapshot();
                string? warning = Warnings.Peek();

                return new IcmpTaskSnapshot(
                    Name: Options.Name,
                    Target: Options.Target,
                    Status: _status,
                    Category: _category,
                    DelayMs: _lastDelayMs,
                    JitterMs: stats.JitterMs,
                    WindowLossRate: stats.WindowLossRate,
                    TotalLossRate: stats.TotalLossRate,
                    Sent: stats.Sent,
                    Received: stats.Received,
                    Timestamp: _lastEventAt,
                    LastEvent: _lastEvent,
                    Warning: warning,
                    WarningCount: Warnings.Count);
            }
        }

        private async Task ProbeLoopAsync(CancellationToken token)
        {
            using Ping ping = new();

            while (!token.IsCancellationRequested)
            {
                long started = Stopwatch.GetTimestamp();

                try
                {
                    await ProbeAsync(ping, token).ConfigureAwait(false);
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

        private async Task ProbeAsync(Ping ping, CancellationToken token)
        {
            PingReply? reply = null;
            string? error = null;

            try
            {
                // .NET 的 (string, CancellationToken) 组合只有 TimeSpan 超时版本，
                // int 超时版本没有 CancellationToken 形参。
                TimeSpan timeout = TimeSpan.FromMilliseconds(Options.TimeoutMs);
                reply = await ping.SendPingAsync(Options.Target, timeout, Payload, null, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 解析失败、权限不足等一律按「未知」计入丢包，避免循环退出；
                // 同时保留异常信息，否则界面上只能看到一个“未知状态”。
                error = ex.Message;
            }

            IPStatus status = reply?.Status ?? IPStatus.Unknown;
            double? rtt = status == IPStatus.Success ? reply!.RoundtripTime : null;

            ApplyResult(status, rtt, error);
        }

        private void ApplyResult(IPStatus status, double? rttMs, string? errorMessage = null)
        {
            IcmpTaskSnapshot snapshot;
            IcmpFaultCategory previousCategory;
            bool statusChanged;
            bool delaySpiked = false;
            bool lossAlarm = false;
            DateTimeOffset now = DateTimeOffset.Now;

            lock (_gate)
            {
                previousCategory = _category;
                statusChanged = status != _status;
                double? previousDelay = _lastDelayMs;

                _statistics.Record(status == IPStatus.Success, rttMs ?? 0);
                _status = status;
                _category = status.Classify();
                _lastDelayMs = rttMs;

                if (status == IPStatus.Success)
                {
                    if (_consecutiveLosses > 0)
                    {
                        _consecutiveLosses = 0;
                        _lossEpisodeStart = null;
                        _lossAlarmRaised = false;
                    }

                    if (Options.SignificantDelayThresholdMs > 0
                        && rttMs is not null
                        && previousDelay is not null
                        && rttMs.Value - previousDelay.Value > Options.SignificantDelayThresholdMs)
                    {
                        delaySpiked = true;
                        _lastEvent = $"延迟波动 {previousDelay.Value:0}ms → {rttMs.Value:0}ms";
                    }
                }
                else
                {
                    _consecutiveLosses++;
                    _lossEpisodeStart ??= now;

                    if (!_lossAlarmRaised && ShouldRaiseLossAlarm(now))
                    {
                        _lossAlarmRaised = true;
                        lossAlarm = true;
                        _lastEvent = $"连续丢包 {_consecutiveLosses} 次（{(now - _lossEpisodeStart.Value).TotalSeconds:0}s）";
                    }
                }

                if (statusChanged)
                {
                    _lastEvent = status == IPStatus.Success
                        ? $"通讯正常 {rttMs.GetValueOrDefault():0}ms"
                        : string.IsNullOrWhiteSpace(errorMessage) ? status.ToChineseString() : $"{status.ToChineseString()}: {errorMessage}";
                }

                if (statusChanged || delaySpiked || lossAlarm)
                    _lastEventAt = now;

                snapshot = BuildSnapshot();
            }

            if (statusChanged)
                Raise(StatusChanged, snapshot, previousCategory);

            if (delaySpiked)
                Raise(DelaySpiked, snapshot, previousCategory);

            if (lossAlarm)
                Raise(LossAlarmRaised, snapshot, previousCategory);
        }

        /// <summary>
        /// 逐个调用订阅者并隔离异常：任何一个订阅方出问题都不能终止探测循环。
        /// </summary>
        private void Raise(EventHandler<IcmpTaskEventArgs>? handler, IcmpTaskSnapshot snapshot, IcmpFaultCategory previousCategory)
        {
            if (handler is null)
                return;

            IcmpTaskEventArgs args = new(snapshot, previousCategory);

            foreach (Delegate callback in handler.GetInvocationList())
            {
                if (callback is not EventHandler<IcmpTaskEventArgs> subscriber)
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

        private bool ShouldRaiseLossAlarm(DateTimeOffset now)
        {
            bool byDuration = Options.PacketLossDurationSeconds > 0
                && _lossEpisodeStart is { } start
                && now - start >= Options.PacketLossDuration;

            bool byCount = Options.PacketLossCount > 0
                && _consecutiveLosses >= Options.PacketLossCount;

            // 两个阈值都配置时需同时满足；只配置一个时按配置的那个判定。
            if (Options.PacketLossDurationSeconds > 0 && Options.PacketLossCount > 0)
                return byDuration && byCount;

            return byDuration || byCount;
        }

        private IcmpTaskSnapshot BuildSnapshot()
        {
            IcmpStatisticsSnapshot stats = _statistics.Snapshot();

            return new IcmpTaskSnapshot(
                Name: Options.Name,
                Target: Options.Target,
                Status: _status,
                Category: _category,
                DelayMs: _lastDelayMs,
                JitterMs: stats.JitterMs,
                WindowLossRate: stats.WindowLossRate,
                TotalLossRate: stats.TotalLossRate,
                Sent: stats.Sent,
                Received: stats.Received,
                Timestamp: _lastEventAt,
                LastEvent: _lastEvent,
                Warning: Warnings.Peek(),
                WarningCount: Warnings.Count);
        }

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
        }
    }
}
