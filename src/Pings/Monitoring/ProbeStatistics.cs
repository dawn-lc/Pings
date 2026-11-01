namespace Pings.Monitoring
{
    /// <summary>
    /// 探测统计快照。
    /// </summary>
    /// <remarks>
    /// 丢包率只统计「完全没有回应」（<see cref="ProbeStatusFacts.CountsAsLoss" />），
    /// 故障率统计所有非成功（端口关闭、DNS 失败、TLS 失败、状态码异常等）。
    /// 两套口径分开，避免「服务返回 503」被当成网络丢包。
    /// </remarks>
    public readonly record struct ProbeStatisticsSnapshot(
        long Sent,
        long Received,
        long Lost,
        long Failed,
        int WindowCount,
        int WindowReceived,
        double WindowLossRate,
        double TotalLossRate,
        double WindowFailureRate,
        double TotalFailureRate,
        double? MinRttMs,
        double? AverageRttMs,
        double? MaxRttMs,
        double? P95RttMs,
        double? LastRttMs,
        double? StdDevMs,
        double? JitterMs,
        int JitterSamples);

    /// <summary>
    /// 单个任务的探测统计：累计计数 + 滑动窗口的丢包率 / 故障率 / RTT 分布 / 抖动。
    /// </summary>
    /// <remarks>
    /// 写入方是探测循环（单线程），读取方是界面刷新（另一线程），读写都通过同一把锁保护。
    ///
    /// 累计计数增量维护；窗口统计在 <see cref="Snapshot" /> 时单遍扫描完成，
    /// 不做增量聚合——窗口回绕时扣减聚合量容易出现漂移，而单遍扫描的开销
    /// （窗口上限 10 万，刷新频率 1Hz）完全可以接受。
    /// </remarks>
    public sealed class ProbeStatistics
    {
        /// <summary>计算分位数时最多排序的样本数，超过则等步长抽样，结果为近似值。</summary>
        public const int PercentileSampleLimit = 4096;

        private const double JitterGain = 1.0 / 16.0;

        private readonly object _gate = new();
        private readonly ProbeSample[] _window;
        private readonly double[] _scratch;

        private int _next;
        private int _windowCount;
        private long _sent;
        private long _received;
        private long _lost;
        private long _failed;
        private double? _lastRttMs;
        private double? _jitterAnchorMs;
        private double _jitterMs;
        private int _jitterSamples;

        public ProbeStatistics(int windowSize)
        {
            if (windowSize < 1)
                throw new ArgumentOutOfRangeException(nameof(windowSize), "窗口大小必须大于 0");

            _window = new ProbeSample[windowSize];
            _scratch = new double[Math.Min(windowSize, PercentileSampleLimit)];
        }

        private readonly record struct ProbeSample(bool Success, bool Loss, double RttMs);

        /// <summary>记录一次探测结果。</summary>
        public void Record(ProbeStatus status, double rttMs = 0)
        {
            if (status == ProbeStatus.Unknown)
                return;

            bool success = status.IsSuccess();
            bool loss = status.CountsAsLoss();
            bool failure = status.IsFailure();

            lock (_gate)
            {
                _sent++;
                if (success)
                    _received++;
                if (loss)
                    _lost++;
                if (failure)
                    _failed++;

                if (success)
                {
                    if (_jitterAnchorMs is { } anchor)
                    {
                        // RFC 3550 风格 EWMA 抖动（增益 1/16）
                        _jitterMs += (Math.Abs(rttMs - anchor) - _jitterMs) * JitterGain;
                        _jitterSamples++;
                    }
                    else
                    {
                        _jitterSamples = 1;
                    }

                    _jitterAnchorMs = rttMs;
                    _lastRttMs = rttMs;
                }
                else
                {
                    // 跨丢包重新起算：丢包前后的 RTT 差不是抖动。
                    _jitterAnchorMs = null;
                    _jitterMs = 0;
                    _jitterSamples = 0;
                }

                _window[_next] = new ProbeSample(success, loss, rttMs);
                _next = (_next + 1) % _window.Length;
                if (_windowCount < _window.Length)
                    _windowCount++;
            }
        }

        public ProbeStatisticsSnapshot Snapshot()
        {
            lock (_gate)
            {
                int successCount = 0;
                int lossCount = 0;
                double sum = 0;
                double sumSquares = 0;
                double? min = null;
                double? max = null;

                for (int i = 0; i < _windowCount; i++)
                {
                    ProbeSample sample = _window[i];

                    if (sample.Success)
                    {
                        successCount++;
                        double value = sample.RttMs;
                        sum += value;
                        sumSquares += value * value;
                        min = min is null || value < min ? value : min;
                        max = max is null || value > max ? value : max;
                    }
                    else if (sample.Loss)
                    {
                        lossCount++;
                    }
                }

                // 故障 = 所有非成功样本（丢包是它的子集），不能漏掉端口关闭 / DNS 失败这类。
                int failureCount = _windowCount - successCount;

                double? average = successCount == 0 ? null : sum / successCount;
                double? stdDev = null;

                if (successCount > 0 && average is { } mean)
                {
                    double variance = (sumSquares / successCount) - (mean * mean);
                    stdDev = Math.Sqrt(Math.Max(0, variance));
                }

                return new ProbeStatisticsSnapshot(
                    Sent: _sent,
                    Received: _received,
                    Lost: _lost,
                    Failed: _failed,
                    WindowCount: _windowCount,
                    WindowReceived: successCount,
                    WindowLossRate: _windowCount == 0 ? 0 : (double)lossCount / _windowCount,
                    TotalLossRate: _sent == 0 ? 0 : (double)_lost / _sent,
                    WindowFailureRate: _windowCount == 0 ? 0 : (double)failureCount / _windowCount,
                    TotalFailureRate: _sent == 0 ? 0 : (double)_failed / _sent,
                    MinRttMs: min,
                    AverageRttMs: average,
                    MaxRttMs: max,
                    P95RttMs: Percentile(95),
                    LastRttMs: _lastRttMs,
                    StdDevMs: stdDev,
                    JitterMs: _jitterSamples >= 2 ? _jitterMs : null,
                    JitterSamples: _jitterSamples);
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                Array.Clear(_window);
                _next = 0;
                _windowCount = 0;
                _sent = 0;
                _received = 0;
                _lost = 0;
                _failed = 0;
                _lastRttMs = null;
                _jitterAnchorMs = null;
                _jitterMs = 0;
                _jitterSamples = 0;
            }
        }

        /// <summary>
        /// 窗口内成功 RTT 的分位数。样本数超过 <see cref="PercentileSampleLimit" />
        /// 时按固定步长抽样，返回的是近似值（调用方已在文档中标注）。
        /// </summary>
        private double? Percentile(int percentile)
        {
            int total = 0;
            for (int i = 0; i < _windowCount; i++)
            {
                if (_window[i].Success)
                    total++;
            }

            if (total == 0)
                return null;

            int count = 0;
            if (total <= _scratch.Length)
            {
                for (int i = 0; i < _windowCount; i++)
                {
                    if (_window[i].Success)
                        _scratch[count++] = _window[i].RttMs;
                }
            }
            else
            {
                int stride = (total + _scratch.Length - 1) / _scratch.Length;
                int seen = 0;
                for (int i = 0; i < _windowCount && count < _scratch.Length; i++)
                {
                    if (!_window[i].Success)
                        continue;

                    if (seen % stride == 0)
                        _scratch[count++] = _window[i].RttMs;
                    seen++;
                }
            }

            Array.Sort(_scratch, 0, count);

            int index = (int)Math.Ceiling(count * percentile / 100.0) - 1;
            return _scratch[Math.Clamp(index, 0, count - 1)];
        }
    }
}
