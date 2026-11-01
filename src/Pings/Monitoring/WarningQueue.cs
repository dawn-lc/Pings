namespace Pings.Monitoring
{
    /// <summary>
    /// 有界、线程安全的告警队列：界面取队头展示，按 C 确认后出队。
    /// 达到容量上限时丢弃最旧的一条，避免长时间无人确认时无限增长。
    /// </summary>
    public sealed class WarningQueue
    {
        private readonly object _gate = new();
        private readonly Queue<string> _items;
        private readonly int _capacity;

        public WarningQueue(int capacity = 32)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于 0");

            _capacity = capacity;
            _items = new Queue<string>(capacity);
        }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _items.Count;
                }
            }
        }

        public void Enqueue(string message)
        {
            lock (_gate)
            {
                if (_items.Count >= _capacity)
                    _items.Dequeue();

                _items.Enqueue(message);
            }
        }

        /// <summary>查看队头，不移除。</summary>
        public string? Peek()
        {
            lock (_gate)
            {
                return _items.Count == 0 ? null : _items.Peek();
            }
        }

        /// <summary>移除并返回队头。</summary>
        public string? Dequeue()
        {
            lock (_gate)
            {
                return _items.Count == 0 ? null : _items.Dequeue();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _items.Clear();
            }
        }
    }
}
