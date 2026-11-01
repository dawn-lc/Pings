namespace Pings.Monitoring
{
    /// <summary>滑窗统计快照。</summary>
    public sealed record IcmpStatisticsSnapshot(
        long Sent,
        long Received,
        long Lost,
        double WindowLossRate,
        double TotalLossRate,
        double? MinRttMs,
        double? AverageRttMs,
        double? MaxRttMs,
        double? LastRttMs,
        double JitterMs);

    /// <summary>
    /// 单个任务的探测统计：总量计数 + 滑动窗口丢包率 + RTT 极值/均值 + 抖动。
    /// </summary>
    /// <remarks>
    /// 写入方是探测循环（单线程），读取方是界面刷新（另一线程），
    /// 因此读与写都通过同一把锁保护，避免读到半更新的窗口。
    /// </remarks>
    public sealed class IcmpStatistics
    {
        private readonly object _gate = new();
        private readonly double?[] _window;

        private int _next;
        private int _windowCount;
        private long _sent;
        private long _received;
        private double _jitterMs;
        private double? _lastRttMs;

        public IcmpStatistics(int windowSize)
        {
            if (windowSize < 1)
                throw new ArgumentOutOfRangeException(nameof(windowSize), "窗口大小必须大于 0");

            _window = new double?[windowSize];
        }

        /// <summary>记录一次探测结果；<paramref name="rttMs"/> 为 null 表示本次丢包。</summary>
        public void Record(double? rttMs) => Record(rttMs.HasValue, rttMs ?? 0);

        /// <summary>
        /// 记录一次探测结果。必须显式传 <paramref name="received"/>：
        /// 回环地址的 RTT 就是 0ms，用 "&gt; 0" 判断会把成功误判成丢包。
        /// </summary>
        public void Record(bool received, double rttMs)
        {
            lock (_gate)
            {
                _sent++;

                if (received)
                {
                    _received++;

                    if (_lastRttMs is { } previous)
                    {
                        // RFC 3550 风格的 EWMA 抖动（增益 1/16 的简化版本）
                        _jitterMs += (Math.Abs(rttMs - previous) - _jitterMs) / 16.0;
                    }

                    _lastRttMs = rttMs;
                }

                _window[_next] = received ? rttMs : null;
                _next = (_next + 1) % _window.Length;
                if (_windowCount < _window.Length)
                    _windowCount++;
            }
        }

        public IcmpStatisticsSnapshot Snapshot()
        {
            lock (_gate)
            {
                double? min = null;
                double? max = null;
                double sum = 0;
                int receivedInWindow = 0;

                for (int i = 0; i < _windowCount; i++)
                {
                    double? rtt = _window[i];
                    if (rtt is null)
                        continue;

                    receivedInWindow++;
                    double value = rtt.Value;
                    sum += value;
                    min = min is null || value < min ? value : min;
                    max = max is null || value > max ? value : max;
                }

                int lostInWindow = _windowCount - receivedInWindow;
                long lostTotal = _sent - _received;

                return new IcmpStatisticsSnapshot(
                    Sent: _sent,
                    Received: _received,
                    Lost: lostTotal,
                    WindowLossRate: _windowCount == 0 ? 0 : (double)lostInWindow / _windowCount,
                    TotalLossRate: _sent == 0 ? 0 : (double)lostTotal / _sent,
                    MinRttMs: min,
                    AverageRttMs: receivedInWindow == 0 ? null : sum / receivedInWindow,
                    MaxRttMs: max,
                    LastRttMs: _lastRttMs,
                    JitterMs: _jitterMs);
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
                _jitterMs = 0;
                _lastRttMs = null;
            }
        }
    }
}
