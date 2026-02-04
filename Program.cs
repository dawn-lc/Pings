using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Channels;

namespace Pings
{
    // Pings 命名空间 - 网络监控应用程序
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
            ObjectDisposedException.ThrowIf(_disposed, nameof(ObservableQueue<T>));
            await _channel.Writer.WriteAsync(item);
            Enqueued?.Invoke(item);
        }

        /// <summary>异步出队元素</summary>
        public async Task<T> DequeueAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(ObservableQueue<T>));
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


    /// <summary>
    /// IPStatus 枚举的扩展方法，提供中文描述
    /// </summary>
    public static class IPStatusExtensions
    {
        enum IcmpFaultCategory
        {
            None,                   // 正常
            ConnectivityLoss,        // 连通性丢失（核心告警）
            RoutingError,            // 路由/寻址问题
            PacketLifetimeExceeded,  // TTL / 生命周期问题
            PacketFormatError,       // 报文格式/协议问题
            ResourceExhausted,       // 本地或网络资源不足
            AccessDenied,            // 被策略/ACL/防火墙拒绝
            HardwareFailure,         // 硬件错误
            Unknown                  // 未知
        }

        static IcmpFaultCategory Classify(IPStatus status) => status
            switch
        {
            // ✅ 正常
            IPStatus.Success
                => IcmpFaultCategory.None,

            // ❌ 连通性丢失（视为同一问题）
            IPStatus.TimedOut
            or IPStatus.DestinationHostUnreachable
            or IPStatus.DestinationNetworkUnreachable
            or IPStatus.DestinationUnreachable
                => IcmpFaultCategory.ConnectivityLoss,

            // 🚫 访问被拒绝 / 策略问题
            IPStatus.DestinationProhibited
            or IPStatus.DestinationPortUnreachable
            or IPStatus.DestinationScopeMismatch
                => IcmpFaultCategory.AccessDenied,

            // 🧭 路由 / 寻址问题
            IPStatus.BadDestination
            or IPStatus.BadRoute
                => IcmpFaultCategory.RoutingError,

            // ⏱ 生命周期 / TTL
            IPStatus.TtlExpired
            or IPStatus.TimeExceeded
            or IPStatus.TtlReassemblyTimeExceeded
                => IcmpFaultCategory.PacketLifetimeExceeded,

            // 📦 报文 / 协议问题
            IPStatus.BadHeader
            or IPStatus.BadOption
            or IPStatus.ParameterProblem
            or IPStatus.UnrecognizedNextHeader
            or IPStatus.IcmpError
                => IcmpFaultCategory.PacketFormatError,

            // 💾 资源耗尽
            IPStatus.NoResources
            or IPStatus.SourceQuench
                => IcmpFaultCategory.ResourceExhausted,

            // 🔧 硬件
            IPStatus.HardwareError
                => IcmpFaultCategory.HardwareFailure,

            // ❓ 未知
            IPStatus.Unknown
            or _
                => IcmpFaultCategory.Unknown
        };

        /// <summary>
        /// 将 IPStatus 转换为中文描述
        /// </summary>
        public static string ToChineseString(this IPStatus status)
        {
            return status switch
            {
                IPStatus.BadDestination => "目标地址错误",
                IPStatus.BadHeader => "头部无效",
                IPStatus.BadOption => "选项无效",
                IPStatus.BadRoute => "路由无效",
                IPStatus.DestinationHostUnreachable => "目标主机不可达",
                IPStatus.DestinationNetworkUnreachable => "目标网络不可达",
                IPStatus.DestinationPortUnreachable => "目标端口不可达",
                IPStatus.DestinationProhibited => "目标禁止访问",
                IPStatus.DestinationScopeMismatch => "目标范围不匹配",
                IPStatus.DestinationUnreachable => "目标不可达",
                IPStatus.HardwareError => "硬件错误",
                IPStatus.IcmpError => "ICMP协议错误",
                IPStatus.NoResources => "网络资源不足",
                IPStatus.PacketTooBig => "数据包过大",
                IPStatus.ParameterProblem => "参数问题",
                IPStatus.SourceQuench => "数据包被放弃",
                IPStatus.Success => "通讯正常",
                IPStatus.TimedOut => "请求超时",
                IPStatus.TimeExceeded => "生存时间过期",
                IPStatus.TtlExpired => "TTL值过期",
                IPStatus.TtlReassemblyTimeExceeded => "重组超时",
                IPStatus.Unknown => "未知状态",
                IPStatus.UnrecognizedNextHeader => "下一标头无法识别",
                _ => "未知错误",
            };
        }
    }

    /// <summary>
    /// ICMP 监控器类，管理多个 ICMP 测试任务并显示监控界面
    /// </summary>
    class ICMPMonitor
    {
        private CancellationTokenSource CancellationTokenSource { get; set; }
        private Logging? Logging { get; set; }
        /// <summary>任务映射表：IP地址 -> 表格行索引</summary>
        private Dictionary<string, int> TaskMap { get; set; }
        /// <summary>任务显示表格</summary>
        public Table TasksTable { get; set; }
        /// <summary>ICMP 测试任务列表</summary>
        public List<ICMPTestTask> Tasks { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="cancellationTokenSource">取消令牌源</param>
        /// <param name="logging">日志记录器</param>
        public ICMPMonitor(CancellationTokenSource cancellationTokenSource, Logging? logging = null)
        {
            CancellationTokenSource = cancellationTokenSource;
            Logging = logging;
            Tasks = [];
            TaskMap = [];
            TasksTable = new() { Caption = new TableTitle("确认警告(C) / 退出(Q)") };
            TasksTable.AddColumns("名称", "IP/域名", "状态", "延迟", "警告/日志");
            TasksTable.Centered();
        }

        public void AddHost(ICMPTestTask newTask)
        {
            TaskMap.Add(newTask.IP, TasksTable.Rows.Add([new Text(newTask.Name), new Text(newTask.IP), new Text(newTask.State.ToChineseString()), new Text($"{newTask.Delay.TotalMilliseconds}ms"), new Text(newTask.LastLog)]));

            newTask.LastLogChanged += (task) =>
            {
                if (!task.IsWarning)
                {
                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog));
                }
            };
            newTask.DelayChanged += (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 3, new Text($"{(int)task.Delay.TotalMilliseconds}ms"));
            };
            newTask.OpenWarning += async (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) 触发警告 当前状态：{task.State.ToChineseString()}");

                TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(await task.Warnings.DequeueAsync(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));
            };
            newTask.ConfirmWarning += async (task) =>
            {
                if (!task.IsWarning)
                {
                    Logging?.Log($"{task.Name}({task.IP}) 解除警告 当前状态：{task.State.ToChineseString()}");

                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog));
                }
                else
                {
                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(await task.Warnings.DequeueAsync(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));
                }
            };
            newTask.StatusChanged += async (task) =>
            {
                // 只有在Delay有效时才记录延迟值
                string delayText = task.Delay > ICMPTestTask.DefaultDelay ? $" {(int)task.Delay.TotalMilliseconds}ms" : "";
                Logging?.Log($"{task.Name}({task.IP}) {task.State.ToChineseString()}{delayText}");

                TasksTable.Rows.Update(TaskMap[task.IP], 2, task.State == IPStatus.Success ? new Text(task.State.ToChineseString()) : new Text(task.State.ToChineseString(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));

                task.LastLog = $"{task.State.ToChineseString()} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ";
                if (task.State != IPStatus.Success) await task.Warnings.EnqueueAsync($"{task.State.ToChineseString()} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
            };
            newTask.DelayExceptionOccurred += (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) 延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(int)task.Delay.TotalMilliseconds}ms");

                task.LastLog = $"延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(int)task.Delay.TotalMilliseconds}ms";
            };

            Tasks.Add(newTask);
        }
    }

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
        /// <summary>状态变化时的事件</summary>
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
        /// <summary>是否有未确认的警告</summary>
        public bool IsWarning => Warnings.Count > 0;

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
                if (State != value)
                {
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
                int pingCounter = 0;
                Stopwatch stopwatch = new();
                using Ping ping = new();
                while (!CTS.Token.IsCancellationRequested)
                {
                    PingReply? reply = null;
                    try
                    {
                        stopwatch.Restart();
                        reply = ping.Send(IP, timeout);
                        stopwatch.Stop();
                        State = reply.Status;
                        Delay = State == IPStatus.Success
                            ? TimeSpan.FromMilliseconds(reply.RoundtripTime)
                            : DefaultDelay;
                    }
                    catch
                    {
                        State = IPStatus.Unknown;
                        Delay = DefaultDelay;
                    }

                    await RecentPackets.EnqueueAsync(State);

                    pingCounter++;
                    if (pingCounter == MaxRecentPackets)
                    {
                        pingCounter = 0;
                    }

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


    /// <summary>
    /// 日志记录器类，提供异步日志写入功能
    /// </summary>
    class Logging : IDisposable
    {
        private bool disposedValue;

        private CancellationTokenSource CTS { get; set; }
        private StreamWriter OutputFile { get; set; }
        /// <summary>日志消息队列</summary>
        private ConcurrentQueue<string>? Logs { get; set; }
        /// <summary>字符串构建器，用于批量写入</summary>
        private StringBuilder? Builder { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="output">日志文件路径</param>
        public Logging(string output)
        {
            CTS = new();
            Logs = new();
            OutputFile = new(Path.GetFullPath(output), true);
            Builder = new StringBuilder();
            Task.Run(async () =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    Write();
                    await Task.Delay(TimeSpan.FromSeconds(1), CTS.Token);
                }
            }, CTS.Token);
        }

        /// <summary>
        /// 将队列中的日志写入文件
        /// </summary>
        private void Write()
        {
            if (!(Logs?.IsEmpty ?? false))
            {
                while (Logs?.TryDequeue(out string? item) ?? false)
                {
                    if (item != null) Builder?.AppendLine(item);
                }
                OutputFile.Write(Builder);
                OutputFile.Flush();
                Builder?.Clear();
            }
        }

        /// <summary>
        /// 记录日志消息
        /// </summary>
        /// <param name="content">日志内容</param>
        public void Log(string content)
        {
            Logs?.Enqueue($"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss}] {content}");
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Write();
                    CTS.Cancel();
                    Builder = null;
                    Logs = null;
                    OutputFile.Dispose();
                    CTS.Dispose();
                }
                disposedValue = true;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// ICMP 任务配置结构，用于配置 ICMP 测试任务的参数
    /// </summary>
    public struct ICMPTaskConfig
    {
        private string name;
        private string ip;
        private int? timeout;
        private int? maxRecentPackets;
        private int? significantDelayThreshold;
        private double? packetLossDuration;
        private int? packetLossCount;

        /// <summary>任务名称</summary>
        public string Name
        {
            get => name ?? throw new ArgumentException("Invalid domain name.");
            set
            {
                if (!IsValidDomainName(value))
                    throw new ArgumentException($"Invalid domain name \"{value}\".");
                name = value;
            }
        }

        /// <summary>目标 IP 地址或域名</summary>
        public string IP
        {
            get => ip;
            set
            {
                if (!IsValidIP(value))
                    throw new ArgumentException($"Invalid IP address \"{value}\".");
                ip = value;
            }
        }

        /// <summary>Ping 超时时间（毫秒）</summary>
        public int Timeout
        {
            get => timeout ?? 1000;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be non-negative.");
                timeout = value;
            }
        }

        /// <summary>最大最近数据包记录数</summary>
        public int MaxRecentPackets
        {
            get => maxRecentPackets ?? 255;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(MaxRecentPackets), "MaxRecentPackets must be non-negative.");
                maxRecentPackets = value;
            }
        }

        /// <summary>显著延迟变化阈值（毫秒）</summary>
        public int SignificantDelayThreshold
        {
            get => significantDelayThreshold ?? 20;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(SignificantDelayThreshold), "SignificantDelayThreshold must be non-negative.");
                significantDelayThreshold = value;
            }
        }

        /// <summary>丢包持续时间阈值（秒）</summary>
        public TimeSpan PacketLossDuration
        {
            get => TimeSpan.FromSeconds(packetLossDuration ?? (Timeout / 1000) * 30);
            set
            {
                if (value.TotalMilliseconds < Timeout)
                    throw new ArgumentOutOfRangeException(nameof(PacketLossDuration), $"PacketLossDuration must be greater than {nameof(Timeout)}.");
                packetLossDuration = value.TotalSeconds;
            }
        }

        /// <summary>丢包计数阈值</summary>
        public int PacketLossCount
        {
            get => packetLossCount ?? 5;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(PacketLossCount), "PacketLossCount must be non-negative.");
                packetLossCount = value;
            }
        }

        /// <summary>
        /// 从字符串数组构造配置
        /// </summary>
        /// <param name="raw">配置字符串数组，格式：[名称] [IP] [超时] [最大记录数] [延迟阈值] [丢包持续时间] [丢包计数]</param>
        public ICMPTaskConfig(params string[] raw)
        {
            if (raw.Length < 1)
                throw new ArgumentException("A valid name is required.");
            name = raw[0];
            if (raw.Length < 2)
                throw new ArgumentException("A valid IP is required.");
            ip = raw[1];
            if (raw.Length > 2 && int.TryParse(raw[2], out int parsedTimeout)) Timeout = parsedTimeout;
            if (raw.Length > 3 && int.TryParse(raw[3], out int parsedMaxRecentPackets)) MaxRecentPackets = parsedMaxRecentPackets;
            if (raw.Length > 4 && int.TryParse(raw[4], out int parsedSignificantDelayThreshold)) SignificantDelayThreshold = parsedSignificantDelayThreshold;
            if (raw.Length > 5 && double.TryParse(raw[5], out double parsedPacketLossDuration)) PacketLossDuration = TimeSpan.FromSeconds(parsedPacketLossDuration);
            if (raw.Length > 6 && int.TryParse(raw[6], out int parsedPacketLossCount)) PacketLossCount = parsedPacketLossCount;
        }

        /// <summary>验证 IP 地址是否有效</summary>
        private static bool IsValidIP(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }

        /// <summary>验证域名是否有效</summary>
        private static bool IsValidDomainName(string name)
        {
            return Uri.CheckHostName(name) != UriHostNameType.Unknown;
        }
    }

    /// <summary>
    /// 主程序类，网络监控应用程序的入口点
    /// </summary>
    internal class Program
    {
        /// <summary>全局取消令牌源，用于控制所有异步任务</summary>
        private static CancellationTokenSource CTS { get; set; } = new();
        /// <summary>全局日志记录器</summary>
        private static Logging Logging { get; set; } = new("Pings.log");

        /// <summary>
        /// 将旧版文本配置文件转换为JSON格式
        /// </summary>
        /// <param name="oldConfigPath">旧版配置文件路径</param>
        /// <param name="newConfigPath">新版JSON配置文件路径</param>
        private static void ConvertOldConfigToJson(string oldConfigPath, string newConfigPath)
        {
            try
            {
                // 读取旧版配置文件
                string[] configLines = [.. File.ReadAllLines(oldConfigPath)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.Split(' ').Length > 1)];

                if (configLines.Length == 0)
                {
                    throw new InvalidOperationException("旧版配置文件为空或格式错误");
                }

                // 创建新的JSON配置
                var appConfig = new AppConfig
                {
                    Tasks = []
                };

                // 转换每个任务
                foreach (var line in configLines)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var taskConfig = new ICMPTaskConfigJson
                        {
                            Name = parts[0],
                            IP = parts[1],
                            Timeout = parts.Length > 2 && int.TryParse(parts[2], out int timeout) ? timeout : 1000,
                            MaxRecentPackets = parts.Length > 3 && int.TryParse(parts[3], out int maxRecentPackets) ? maxRecentPackets : 255,
                            SignificantDelayThreshold = parts.Length > 4 && int.TryParse(parts[4], out int significantDelayThreshold) ? significantDelayThreshold : 20,
                            PacketLossDuration = parts.Length > 5 && double.TryParse(parts[5], out double packetLossDuration) ? packetLossDuration : 30.0,
                            PacketLossCount = parts.Length > 6 && int.TryParse(parts[6], out int packetLossCount) ? packetLossCount : 5
                        };

                        appConfig.Tasks.Add(taskConfig);
                    }
                }

                // 保存为JSON格式
                ConfigLoader.SaveConfig(appConfig, newConfigPath);
                AnsiConsole.WriteLine($"成功将旧版配置文件转换为JSON格式: {newConfigPath}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"配置文件转换失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 应用程序主入口点
        /// </summary>
        /// <param name="args">命令行参数，第一个参数为配置文件路径（可选）</param>
        static void Main(string[] args)
        {
            // 获取程序版本并设置控制台标题
            string version = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? throw new Exception("运行环境异常！")).FileVersion?[..^2] ?? throw new Exception("程序文件异常！");
            Console.Title = $"Pings {version}";

            // 配置文件路径处理
            string configPath = Path.GetFullPath(args.Length > 0 ? args[0] : "config.json");
            if (!File.Exists(configPath))
            {
                AnsiConsole.WriteLine($"JSON配置文件 {configPath} 不存在。");
                if (AnsiConsole.Confirm($"是否创建默认JSON配置文件？"))
                {
                    ConfigLoader.CreateDefaultConfig(configPath);
                    AnsiConsole.WriteLine($"已创建默认配置文件: {configPath}");
                }
                else
                {
                    // 检查旧版配置文件
                    string oldConfigPath = Path.ChangeExtension(configPath, ".txt");
                    if (File.Exists(oldConfigPath))
                    {
                        AnsiConsole.WriteLine($"检测到旧版配置文件 {oldConfigPath}，正在转换为JSON格式...");
                        ConvertOldConfigToJson(oldConfigPath, configPath);
                    }
                    else
                    {
                        AnsiConsole.WriteLine("未找到配置文件，程序退出。");
                        return;
                    }
                }
            }

            try
            {
                // 加载JSON配置
                AppConfig appConfig = ConfigLoader.LoadConfig(configPath);

                // 验证配置
                var validationResult = ConfigLoader.ValidateConfig(appConfig);
                if (!validationResult.IsValid)
                {
                    AnsiConsole.WriteLine($"配置文件验证失败: {validationResult.ErrorMessage}");
                    AnsiConsole.WriteLine("按任意键退出...");
                    Console.ReadKey(true);
                    return;
                }

                // 更新日志配置
                if (appConfig.Logging.LogFilePath != "Pings.log")
                {
                    Logging = new Logging(appConfig.Logging.LogFilePath);
                }

                // 创建 ICMP 监控器
                ICMPMonitor monitor = new(CTS, Logging);

                // 从JSON配置创建任务
                foreach (var taskConfig in appConfig.Tasks)
                {
                    monitor.AddHost(new(CTS, ConfigLoader.ToICMPTaskConfig(taskConfig)));
                }

                // 清屏并显示监控界面
                AnsiConsole.Clear();
                AnsiConsole.WriteLine();

                // 启动实时表格显示
                Task display = AnsiConsole.Live(monitor.TasksTable).StartAsync(async ctx =>
                {
                    while (!CTS.Token.IsCancellationRequested)
                    {
                        monitor.TasksTable.Title = new TableTitle($"{DateTime.Now:yyyy年MM月dd日 HH:mm:ss} 网络监测");
                        ctx.Refresh();
                        await Task.Delay(TimeSpan.FromSeconds(1), CTS.Token);
                    }
                });

                // 主事件循环：处理键盘输入
                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        // Q键：退出程序
                        CTS.Cancel();
                        break;
                    }
                    if (key.Key == ConsoleKey.C)
                    {
                        // C键：确认所有警告
                        foreach (var task in monitor.Tasks.FindAll(i => i.IsWarning))
                        {
                            _ = task.Warnings.DequeueAsync();
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // 任务取消异常：正常退出
                return;
            }
            catch (Exception e)
            {
                // 其他异常：显示错误信息
                AnsiConsole.WriteLine(e.ToString());
                AnsiConsole.WriteLine($"按任意键退出...");
                Console.ReadKey(true);
                return;
            }
        }
    }
}
