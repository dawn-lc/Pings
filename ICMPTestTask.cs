using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Pings
{
    /// <summary>
    /// ICMP 测试任务类，负责执行单个目标的 Ping 测试
    /// 通过事件机制通知状态变化
    /// </summary>
    public class ICMPTestTask
    {
        /// <summary>默认延迟值（-1ms 表示无效延迟）</summary>
        public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(-1);

        /// <summary>触发警告时的事件</summary>
        public event Action<ICMPTestTask>? OpenWarning;
        /// <summary>确认警告时的事件</summary>
        public event Action<ICMPTestTask>? ConfirmWarning;
        /// <summary>最后日志变化时的事件</summary>
        public event Action<ICMPTestTask>? LastLogChanged;
        /// <summary>状态分类变化时的事件（只在分类改变时触发）</summary>
        public event Action<ICMPTestTask>? StatusChanged;
        /// <summary>延迟变化时的事件</summary>
        public event Action<ICMPTestTask>? DelayChanged;
        /// <summary>延迟异常时的事件（显著延迟变化）</summary>
        public event Action<ICMPTestTask>? DelayExceptionOccurred;

        /// <summary>任务名称</summary>
        public string Name { get; init; }
        /// <summary>目标 IP 地址或域名</summary>
        public string IP { get; init; }
        /// <summary>最大最近数据包记录数</summary>
        public int MaxRecentPackets { get; init; }
        /// <summary>显著延迟变化阈值</summary>
        public TimeSpan SignificantDelayThreshold { get; init; }
        /// <summary>前一次延迟值</summary>
        public TimeSpan PreviousDelay { get; set; } = DefaultDelay;
        /// <summary>警告消息队列</summary>
        public ObservableQueue<string> Warnings { get; set; }
        /// <summary>最近数据包状态队列（用于统计）</summary>
        private ObservableQueue<IPStatus> RecentPackets { get; set; }

        private string? lastLog;
        /// <summary>最后日志信息</summary>
        public string LastLog
        {
            get
            {
                return lastLog ?? "暂无日志";
            }
            set
            {
                if (LastLog != value)
                {
                    lastLog = value;
                    LastLogChanged?.Invoke(this);
                }
            }
        }

        private IPStatus? previousState;
        /// <summary>前一次状态</summary>
        public IPStatus PreviousState => previousState ?? IPStatus.Unknown;

        private IPStatus? state;
        /// <summary>当前 ICMP 状态</summary>
        public IPStatus State
        {
            get
            {
                return state ?? IPStatus.Unknown;
            }
            set
            {
                _ = RecentPackets.EnqueueAsync(value);
                if (State != value)
                {
                    previousState = state ?? IPStatus.Unknown;
                    state = value;
                    StatusChanged?.Invoke(this);
                }
            }
        }

        private TimeSpan? delay;
        /// <summary>当前延迟值</summary>
        public TimeSpan Delay
        {
            get
            {
                return delay ?? DefaultDelay;
            }
            set
            {
                if (Delay != value)
                {
                    if (value > DefaultDelay) PreviousDelay = Delay;
                    delay = value;
                    DelayChanged?.Invoke(this);
                    if (IsSignificantDelayChange())
                    {
                        DelayExceptionOccurred?.Invoke(this);
                    }
                }
            }
        }

        /// <summary>
        /// 检查是否发生显著延迟变化
        /// </summary>
        private bool IsSignificantDelayChange()
        {
            return State == IPStatus.Success
                && Delay > DefaultDelay
                && PreviousDelay > DefaultDelay
                && Delay > PreviousDelay
                && (Delay - PreviousDelay).Duration() > SignificantDelayThreshold;
        }

        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip, int timeout, int maxRecentPackets, int significantDelayThreshold)
        {
            Name = name;
            IP = ip;
            MaxRecentPackets = maxRecentPackets;
            SignificantDelayThreshold = TimeSpan.FromMilliseconds(significantDelayThreshold);

            Warnings = new();
            RecentPackets = new(maxRecentPackets);

            // 初始化状态和延迟
            State = IPStatus.Unknown;
            Delay = DefaultDelay;
            PreviousDelay = DefaultDelay;

            Warnings.Enqueued += warning => OpenWarning?.Invoke(this);
            Warnings.Dequeued += warning => ConfirmWarning?.Invoke(this);

            Task.Run(async () =>
            {
                Stopwatch stopwatch = new();
                using Ping ping = new();
                while (!CTS.Token.IsCancellationRequested)
                {
                    stopwatch.Restart();
                    try
                    {
                        PingReply reply = ping.Send(IP, timeout);
                        Delay = reply.Status == IPStatus.Success ? TimeSpan.FromMilliseconds(reply.RoundtripTime) : DefaultDelay;
                        State = reply.Status;
                    }
                    catch
                    {
                        Delay = DefaultDelay;
                        State = IPStatus.Unknown;
                    }

                    stopwatch.Stop();
                    var sleep = TimeSpan.FromMilliseconds(timeout) - stopwatch.Elapsed;
                    await Task.Delay(sleep > TimeSpan.Zero && sleep <= TimeSpan.FromMilliseconds(timeout) ? sleep : TimeSpan.FromMilliseconds(timeout), CTS.Token);
                }
            }, CTS.Token);
        }
        public ICMPTestTask(CancellationTokenSource CTS, ICMPTaskConfig config) : this(CTS, config.Name, config.IP, config.Timeout, config.MaxRecentPackets, config.SignificantDelayThreshold) { }
        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip, int timeout, int maxRecentPackets) : this(CTS, name, ip, timeout, maxRecentPackets, 20) { }
        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip, int timeout) : this(CTS, name, ip, timeout, 255, 20) { }
        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip) : this(CTS, name, ip, 1000, 255, 20) { }
        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip, int? timeout = 1000, int? maxRecentPackets = 255, int? significantDelayThreshold = 20) : this(CTS, name, ip, timeout ?? 1000, maxRecentPackets ?? 255, significantDelayThreshold ?? 20) { }
        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip, int? timeout = 1000, int? maxRecentPackets = 255) : this(CTS, name, ip, timeout ?? 1000, maxRecentPackets ?? 255) { }
        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip, int? timeout = 1000) : this(CTS, name, ip, timeout ?? 1000) { }
    }
}