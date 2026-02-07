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

        private readonly Channel<T> channel;
        private bool disposed;

        private T? cachedHead;
        private bool hasCachedHead;
        private readonly SemaphoreSlim peekLock = new(1, 1);

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">通道选项</param>
        /// <param name="capacity">队列容量</param>
        public ObservableQueue(ChannelOptions? options = null, int? capacity = null)
        {
            if (options is null && capacity is null)
            {
                channel = Channel.CreateUnbounded<T>();
                return;
            }

            if (options is null && capacity is not null && capacity > 0)
            {
                channel = Channel.CreateBounded<T>((int)capacity);
                IsBounded = true;
                return;
            }

            if (options is null && capacity is not null && capacity < 0)
                throw new ArgumentException("Queue type is bounded, but the capacity parameter less than zero.", nameof(capacity));

            if (options is BoundedChannelOptions boundedOptions)
            {
                channel = Channel.CreateBounded<T>(boundedOptions);
                IsBounded = true;
            }
            else if (options is UnboundedChannelOptions unboundedOptions)
            {
                channel = Channel.CreateUnbounded<T>(unboundedOptions);
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
            await channel.Writer.WriteAsync(item);
            Enqueued?.Invoke(item);
        }

        /// <summary>异步出队元素</summary>
        public async Task<T> DequeueAsync()
        {
            await peekLock.WaitAsync();
            try
            {
                T item;

                // 如果有缓存，优先返回缓存
                if (hasCachedHead)
                {
                    item = cachedHead!;
                    hasCachedHead = false;
                    cachedHead = default!;
                }
                else
                {
                    item = await channel.Reader.ReadAsync();
                }

                Dequeued?.Invoke(item);
                return item;
            }
            finally
            {
                peekLock.Release();
            }
        }

        /// <summary>
        /// 查看队头元素（不会移除）</summary>
        public async Task<T> PeekAsync()
        {
            await peekLock.WaitAsync();
            try
            {
                if (hasCachedHead)
                    return cachedHead!;

                // 从 Channel 读取一个元素并缓存
                var item = await channel.Reader.ReadAsync();
                cachedHead = item;
                hasCachedHead = true;

                return item;
            }
            finally
            {
                peekLock.Release();
            }
        }

        /// <summary>队列中元素数量（包含缓存）</summary>
        public int Count => channel.Reader.Count + (hasCachedHead ? 1 : 0);

        /// <summary>释放资源</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>释放资源实现</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    channel.Writer.Complete();
                    peekLock.Dispose();
                }
                disposed = true;
            }
        }
    }
}