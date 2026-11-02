using Pings.Properties;

namespace Pings.Monitoring
{
    /// <summary>探测事件类型。</summary>
    public enum ProbeEventKind
    {
        /// <summary>故障类别发生变化（含首次故障与恢复）。</summary>
        StatusChanged,
        /// <summary>从故障恢复。</summary>
        Recovered,
        /// <summary>同一故障类别内的表现切换（超时 ↔ 目标主机不可达）；默认只供 verbose 日志。</summary>
        StatusDetail,
        /// <summary>延迟显著升高。</summary>
        DelaySpike,
        /// <summary>中断告警（疑似 / 确认）。</summary>
        LossAlarm,
        /// <summary>抖动链路（窗口内多次短时中断）。</summary>
        Flapping
    }

    /// <summary>一次探测产生的事件。</summary>
    public readonly record struct ProbeEvent(ProbeEventKind Kind, ProbeSeverity Severity, string Message);

    /// <summary>
    /// 探测状态机：把连续的 <see cref="ProbeResult" /> 变成事件流。
    /// </summary>
    /// <remarks>
    /// 纯逻辑，不做 I/O、不写日志、不碰界面，便于用假 <see cref="TimeProvider" /> 测试。
    ///
    /// 设计要点：
    /// <list type="bullet">
    /// <item>状态事件只在「故障类别」变化时产生（<see cref="ProbeFaultCategory" /> 就是去重粒度）。
    ///   同一类别内的表现切换（<c>Timeout ↔ NoReply</c>、<c>Timeout ↔ Unreachable</c>）只更新快照，
    ///   另发一条仅 verbose 可见的明细事件，不重复写日志、不进告警队列。</item>
    /// <item>中断告警是两级升级：先按次数给出疑似（Warning），再按时长确认（Critical）。
    ///   旧实现要求两个阈值同时满足，默认配置下 5 次丢包必须等满 30 秒才报警。</item>
    /// <item>时长一律用单调时钟计算，系统校时不会让 episode 时长跳变。</item>
    /// <item>延迟波动基于 RFC 6298 的 SRTT / RTTVAR 双 EWMA，带预热、连续确认与施密特回差。</item>
    /// </list>
    /// </remarks>
    public sealed class ProbeStateMachine
    {
        private const double SrttGain = 1.0 / 8.0;
        private const double RttVarGain = 1.0 / 4.0;

        private enum OutageState
        {
            Idle,
            Suspect,
            Confirmed
        }

        private readonly ProbeTaskOptions _options;
        private readonly TimeProvider _time;
        private readonly Queue<long> _shortEpisodes = new();

        private ProbeStatus _status = ProbeStatus.Unknown;
        private ProbeFaultCategory _category = ProbeFaultCategory.None;
        private ProbeSeverity _severity = ProbeSeverity.None;
        private string? _lastEvent;
        private ProbeSeverity _lastEventSeverity = ProbeSeverity.None;
        private DateTimeOffset _lastEventAt = DateTimeOffset.Now;

        private OutageState _outage = OutageState.Idle;
        private int _consecutiveOutages;
        private long? _episodeStart;
        private bool _flapAlarmActive;

        private double? _srtt;
        private double _rttvar;
        private int _successSamples;
        private int _spikeStreak;
        private bool _spikeActive;

        public ProbeStateMachine(ProbeTaskOptions options, TimeProvider? time = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _time = time ?? TimeProvider.System;
        }

        public ProbeStatus Status => _status;

        public ProbeFaultCategory Category => _category;

        public ProbeSeverity Severity => _severity;

        /// <summary>最近一次事件文案；尚无事件时返回当前语言下的「暂无事件」。</summary>
        public string LastEvent => _lastEvent ?? Resources.EventNone;

        public DateTimeOffset LastEventAt => _lastEventAt;

        /// <summary>当前中断 episode 已持续的秒数；没有进行中的 episode 时为 0。</summary>
        public double EpisodeSeconds
            => _episodeStart is { } start ? Math.Max(0, _time.GetElapsedTime(start).TotalSeconds) : 0;

        /// <summary>
        /// 应用一次探测结果并返回本次产生的事件（无事件时返回空数组）。
        /// </summary>
        public ProbeEvent[] Apply(ProbeResult result)
        {
            List<ProbeEvent>? events = null;

            ProbeStatus previousStatus = _status;
            ProbeFaultCategory previousCategory = _category;
            ProbeFaultCategory category = result.Status.Category();
            bool firstResult = previousStatus == ProbeStatus.Unknown;
            long now = _time.GetTimestamp();

            // 1. 中断升级状态机
            if (result.Status.IsSuccess())
                EndOutage(now, recovered: true, ref events);
            else if (result.Status.CountsAsOutage())
                EscalateOutage(now, ref events);
            else if (result.Status.EndsOutage())
                EndOutage(now, recovered: false, ref events);

            // 2. 延迟基线。恢复当次不判波动：基线已过期，报出来的“升高”没有意义。
            if (result.Status.IsSuccess() && result.RttMs is { } rtt)
                EvaluateDelay(rtt, allowSpike: previousCategory == ProbeFaultCategory.None, ref events);

            // 3. 状态与类别
            _status = result.Status;
            _category = category;
            _severity = result.Status.Severity();

            bool categoryChanged = category != previousCategory;
            bool statusChanged = result.Status != previousStatus;

            if (categoryChanged)
            {
                events ??= [];
                events.Add(new ProbeEvent(
                    result.Status.IsSuccess() ? ProbeEventKind.Recovered : ProbeEventKind.StatusChanged,
                    _severity,
                    Describe(result)));
            }
            else if (statusChanged && !firstResult)
            {
                // 类别没变、只是具体表现变了：典型场景是设备离线时 ARP 缓存在「有旧条目」与
                // 「重新解析失败」之间翻转，探测结果就会在 请求超时 ↔ 目标主机不可达 之间跳。
                // 这不是两次状态变化，只是同一个故障的两个观测面。
                // 只发一条低优先级明细，供 --verbose 查看，不进日志与告警队列。
                events ??= [];
                events.Add(new ProbeEvent(ProbeEventKind.StatusDetail, ProbeSeverity.Info, Describe(result)));
            }

            // 4. 最近事件：高优先级不被低优先级覆盖
            if (events is { Count: > 0 })
            {
                ProbeEvent selected = events[0];
                foreach (ProbeEvent candidate in events)
                {
                    if (candidate.Severity > selected.Severity)
                        selected = candidate;
                }

                if (categoryChanged || selected.Severity >= _lastEventSeverity)
                    SetLastEvent(selected.Message, selected.Severity);
            }
            else if (firstResult)
            {
                SetLastEvent(Describe(result), _severity);
            }

            return events is null ? [] : [.. events];
        }

        private void EscalateOutage(long now, ref List<ProbeEvent>? events)
        {
            _consecutiveOutages++;
            _episodeStart ??= now;

            double seconds = EpisodeSeconds;
            bool hasCount = _options.PacketLossCount > 0;
            bool hasDuration = _options.PacketLossDurationSeconds > 0;

            if (_outage == OutageState.Idle)
            {
                if (hasCount && _consecutiveOutages >= _options.PacketLossCount)
                {
                    // 还配了时长阈值：先给疑似告警，达到时长后升级为确认。
                    _outage = hasDuration ? OutageState.Suspect : OutageState.Confirmed;
                    RaiseAlarm(
                        ref events,
                        hasDuration ? ProbeSeverity.Warning : ProbeSeverity.Critical,
                        string.Format(Resources.AlarmOutageCount, _consecutiveOutages));
                }
                else if (!hasCount && hasDuration && seconds >= _options.PacketLossDurationSeconds)
                {
                    _outage = OutageState.Confirmed;
                    RaiseAlarm(ref events, ProbeSeverity.Critical, string.Format(Resources.AlarmOutageSeconds, seconds));
                }
            }

            if (_outage == OutageState.Suspect && seconds >= _options.PacketLossDurationSeconds)
            {
                _outage = OutageState.Confirmed;
                RaiseAlarm(ref events, ProbeSeverity.Critical, string.Format(Resources.AlarmOutageConfirmed, seconds));
            }
        }

        private void EndOutage(long now, bool recovered, ref List<ProbeEvent>? events)
        {
            bool hadEpisode = _consecutiveOutages > 0;
            bool wasConfirmed = _outage == OutageState.Confirmed;

            _outage = OutageState.Idle;
            _consecutiveOutages = 0;
            _episodeStart = null;

            if (wasConfirmed)
                _flapAlarmActive = false;

            // 已经确认过的长故障不算「短故障」；DNS 失败等未发出探测的结果也不算恢复。
            if (!recovered || !hadEpisode || wasConfirmed)
                return;

            _shortEpisodes.Enqueue(now);
            PruneShortEpisodes(now);

            if (!_flapAlarmActive
                && _options.LossFlapCount > 0
                && _options.LossFlapWindowSeconds > 0
                && _shortEpisodes.Count >= _options.LossFlapCount)
            {
                _flapAlarmActive = true;
                events ??= [];
                events.Add(new ProbeEvent(
                    ProbeEventKind.Flapping,
                    ProbeSeverity.Warning,
                    string.Format(Resources.AlarmFlapping, _shortEpisodes.Count, $"{_options.LossFlapWindowSeconds:0}")));
            }
        }

        private void PruneShortEpisodes(long now)
        {
            if (_options.LossFlapWindowSeconds <= 0)
            {
                _shortEpisodes.Clear();
                _flapAlarmActive = false;
                return;
            }

            TimeSpan window = _options.LossFlapWindow;
            while (_shortEpisodes.Count > 0 && _time.GetElapsedTime(_shortEpisodes.Peek(), now) > window)
                _shortEpisodes.Dequeue();

            if (_flapAlarmActive && _shortEpisodes.Count < _options.LossFlapCount)
                _flapAlarmActive = false;
        }

        private void EvaluateDelay(double rtt, bool allowSpike, ref List<ProbeEvent>? events)
        {
            if (_srtt is null)
            {
                _srtt = rtt;
                _rttvar = rtt / 2;
                _successSamples = 1;
                return;
            }

            double srttBefore = _srtt.Value;
            bool tracking = allowSpike && _options.SignificantDelayThresholdMs > 0;
            bool overLimit = false;

            if (tracking && _successSamples >= ProbeTaskOptions.DelaySpikeWarmupSamples)
            {
                double limit = Math.Max(_options.SignificantDelayThresholdMs, 4 * _rttvar);
                overLimit = rtt > srttBefore + limit;

                if (overLimit)
                {
                    _spikeStreak++;

                    if (!_spikeActive && _spikeStreak >= ProbeTaskOptions.DelaySpikeConfirmSamples)
                    {
                        _spikeActive = true;
                        events ??= [];
                        events.Add(new ProbeEvent(
                            ProbeEventKind.DelaySpike,
                            ProbeSeverity.Info,
                            string.Format(Resources.EventDelaySpike, $"{srttBefore:0}", $"{rtt:0}")));
                    }
                }
                else
                {
                    _spikeStreak = 0;

                    // 施密特回差：只有回落到半个阈值以内才解除，避免在边界反复触发。
                    if (_spikeActive && rtt <= srttBefore + limit / 2)
                        _spikeActive = false;
                }
            }

            // 超限样本不参与基线更新：否则一次尖峰会把 RTTVAR 撑大，
            // 紧随其后的同样高的样本立刻变得“正常”，连续确认永远凑不满。
            if (!overLimit)
            {
                _rttvar = (1 - RttVarGain) * _rttvar + RttVarGain * Math.Abs(srttBefore - rtt);
                _srtt = (1 - SrttGain) * srttBefore + SrttGain * rtt;
            }

            _successSamples++;
        }

        private static void RaiseAlarm(ref List<ProbeEvent>? events, ProbeSeverity severity, string message)
        {
            events ??= [];
            events.Add(new ProbeEvent(ProbeEventKind.LossAlarm, severity, message));
        }

        private void SetLastEvent(string message, ProbeSeverity severity)
        {
            _lastEvent = message;
            _lastEventSeverity = severity;
            _lastEventAt = DateTimeOffset.Now;
        }

        private static string StatusTextOf(ProbeResult result)
            => string.IsNullOrWhiteSpace(result.Detail) ? result.Status.ToDisplayString() : result.Detail!;

        private static string Describe(ProbeResult result)
        {
            if (result.Status.IsSuccess())
                return string.Format(Resources.EventSuccess, StatusTextOf(result), $"{result.RttMs.GetValueOrDefault():0}");

            string text = result.Status.ToDisplayString();

            // 传输层给的 Detail 可能与状态文案完全一样，避免出现「请求超时: 请求超时」。
            return string.IsNullOrWhiteSpace(result.Detail) || result.Detail == text
                ? text
                : string.Format(Resources.EventFailure, text, result.Detail);
        }
    }
}
