namespace Pings.Monitoring
{
    /// <summary>
    /// 有界、线程安全的告警队列：界面取队头展示，按 C 确认后出队。
    /// </summary>
    /// <remarks>
    /// 与旧实现的差异：
    /// <list type="bullet">
    /// <item>相邻重复消息合并为一条并记录重复次数（同一故障不会刷满队列）；</item>
    /// <item>容量溢出时丢弃最旧的一条并累计 <see cref="Dropped" />，
    ///   界面据此提示「有告警被丢弃」，而不是静默丢失。</item>
    /// </list>
    /// </remarks>
    public sealed class WarningQueue
    {
        private readonly object _gate = new();
        private readonly LinkedList<Entry> _items = new();
        private readonly int _capacity;
        private long _dropped;

        public WarningQueue(int capacity = 32)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于 0");

            _capacity = capacity;
        }

        private readonly record struct Entry(string Message, int Count);

        /// <summary>去重后的条数。</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                    return _items.Count;
            }
        }

        /// <summary>因容量溢出被丢弃的条数。</summary>
        public long Dropped
        {
            get
            {
                lock (_gate)
                    return _dropped;
            }
        }

        public void Enqueue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            lock (_gate)
            {
                if (_items.Last is { } last && last.Value.Message == message)
                {
                    last.Value = last.Value with { Count = last.Value.Count + 1 };
                    return;
                }

                while (_items.Count >= _capacity)
                {
                    _items.RemoveFirst();
                    _dropped++;
                }

                _items.AddLast(new Entry(message, 1));
            }
        }

        /// <summary>查看队头，不移除。</summary>
        public string? Peek()
        {
            lock (_gate)
                return _items.First is { } first ? Format(first.Value) : null;
        }

        /// <summary>移除并返回队头。</summary>
        public string? Dequeue()
        {
            lock (_gate)
            {
                if (_items.First is not { } first)
                    return null;

                _items.RemoveFirst();
                return Format(first.Value);
            }
        }

        public void Clear()
        {
            lock (_gate)
                _items.Clear();
        }

        private static string Format(Entry entry)
            => entry.Count > 1 ? $"{entry.Message} ×{entry.Count}" : entry.Message;
    }
}
