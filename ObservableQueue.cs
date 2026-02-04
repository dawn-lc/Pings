using System.Threading.Channels;

namespace Pings
{
    /// <summary>
    /// 可观察队列类，基于 System.Threading.Channels 实现
    /// 提供入队和出队事件通知功能
    /// </summary>
    /// <typeparam name="T">队列元素类型</typeparam>
    public class ObservableQueue<T> : IDisposable
    {
        /// <summary>是否是有界队列</summary>
        public bool IsBounded { get; init; } = false;
        private readonly Channel<T> _channel;
        private bool _disposed;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">通道选项</param>
        /// <param name="capacity">队列容量</param>
        public ObservableQueue(ChannelOptions? options = null, int? capacity = null)
        {
            if (options is null && capacity is null)
            {
                _channel = Channel.CreateUnbounded<T>();
                return;
            }
            if (options is null && capacity is not null && capacity > 0)
            {
                _channel = Channel.CreateBounded<T>((int)capacity);
                IsBounded = true;
                return;
            }
            if (options is null && capacity is not null && capacity < 0)
                throw new ArgumentException("Queue type is bounded, but the capacity parameter less than zero.", nameof(capacity));

            if (options is BoundedChannelOptions boundedOptions)
            {
                _channel = Channel.CreateBounded<T>(boundedOptions);
                IsBounded = true;
            }
            else if (options is UnboundedChannelOptions unboundedOptions)
            {
                _channel = Channel.CreateUnbounded<T>(unboundedOptions);
            }
            else
            {
                throw new ArgumentNullException(nameof(options), "Unable to confirm queue type.");
            }
        }
        /// <summary>默认构造函数，创建无界队列</summary>
        public ObservableQueue() : this(null, null) { }
        /// <summary>创建指定容量的有界队列</summary>
        public ObservableQueue(int capacity) : this(null, capacity) { }
        /// <summary>使用指定选项创建队列</summary>
        public ObservableQueue(ChannelOptions options) : this(options, null) { }

        /// <summary>元素入队时触发的事件</summary>
        public event Action<T>? Enqueued;
        /// <summary>元素出队时触发的事件</summary>
        public event Action<T>? Dequeued;

        /// <summary>异步入队元素</summary>
        public async Task EnqueueAsync(T item)
        {
            await _channel.Writer.WriteAsync(item);
            Enqueued?.Invoke(item);
        }

        /// <summary>异步出队元素</summary>
        public async Task<T> DequeueAsync()
        {
            T item = await _channel.Reader.ReadAsync();
            Dequeued?.Invoke(item);
            return item;
        }

        /// <summary>队列中元素数量</summary>
        public int Count => _channel.Reader.Count;

        /// <summary>释放资源</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>释放资源实现</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _channel.Writer.Complete();
                }
                _disposed = true;
            }
        }
    }
}