﻿using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
namespace Pings
{
    public class ObservableQueue<T> : Queue<T>
    {
        public event Action<T>? Enqueued;
        public event Action<T>? Dequeued;
        public new void Enqueue(T item)
        {
            base.Enqueue(item);
            Enqueued?.Invoke(item);
        }
        public new T Dequeue()
        {
            T item = base.Dequeue();
            Dequeued?.Invoke(item);
            return item;
        }
    }
    public static class IPStatusExtensions
    {
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
    class ICMPMonitor
    {
        private CancellationTokenSource CancellationTokenSource { get; set; }
        private Logging? Logging { get; set; }
        private Dictionary<string, int> TaskMap { get; set; }
        public Table TasksTable { get; set; }
        public List<ICMPTestTask> Tasks { get; set; }

        public ICMPMonitor(CancellationTokenSource cancellationTokenSource, Logging? logging = null)
        {
            CancellationTokenSource = cancellationTokenSource;
            Logging = logging;
            Tasks = [];
            TaskMap = [];
            TasksTable = new() { Caption = new TableTitle("当前丢包(L) / 确认警告(C) / 退出(Q)") };
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
            newTask.OpenWarning += (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) 触发警告 当前状态：{task.State.ToChineseString()}");

                TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.Warnings.Peek(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));
            };
            newTask.ConfirmWarning += (task) =>
            {
                if (!task.IsWarning)
                {
                    Logging?.Log($"{task.Name}({task.IP}) 解除警告 当前状态：{task.State.ToChineseString()}");

                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog));
                }
                else
                {
                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.Warnings.Peek(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));
                }
            };
            newTask.StatusChanged += (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) {task.State.ToChineseString()} {(int)task.Delay.TotalMilliseconds}ms");

                TasksTable.Rows.Update(TaskMap[task.IP], 2, new Text(task.State.ToChineseString()));

                task.LastLog = $"{task.State.ToChineseString()} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ";
                if (task.State != IPStatus.Success) task.Warnings.Enqueue($"{task.State.ToChineseString()} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
            };
            newTask.DelayExceptionOccurred += (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) 延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(int)task.Delay.TotalMilliseconds}ms");

                task.LastLog = $"延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(int)task.Delay.TotalMilliseconds}ms";
            };
            newTask.RecentLossRateRecorded += (task) =>
            {
                if (task.RecentLossRate > (double)1 / task.MaxRecentPackets) Logging?.Log($"丢包率 {task.RecentLossRate:F2}%");

                task.LastLog = $"丢包率 {task.RecentLossRate:F2}%";
            };
            Tasks.Add(newTask);
        }
    }

    public class ICMPTestTask
    {
        public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(-1);

        public event Action<ICMPTestTask>? OpenWarning;
        public event Action<ICMPTestTask>? ConfirmWarning;
        public event Action<ICMPTestTask>? LastLogChanged;
        public event Action<ICMPTestTask>? StatusChanged;
        public event Action<ICMPTestTask>? DelayChanged;
        public event Action<ICMPTestTask>? DelayExceptionOccurred;
        public event Action<ICMPTestTask>? RecentLossRateRecorded;

        public string Name { get; init; }
        public string IP { get; init; }
        public int MaxRecentPackets { get; init; }
        public TimeSpan SignificantDelayThreshold { get; init; }
        public TimeSpan PreviousDelay { get; set; } = DefaultDelay;
        public ObservableQueue<string> Warnings { get; set; } = new();
        private Queue<IPStatus> RecentPackets { get; set; } = new();

        public bool IsWarning => Warnings.Count > 0;
        public double RecentLossRate => RecentPackets.Count == 0 ? 0 : (double)RecentPackets.Count(status => status != IPStatus.Success) / RecentPackets.Count * 100;

        private string? lastLog;
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

        private bool IsSignificantDelayChange()
        {
            return State == IPStatus.Success
                && Delay > DefaultDelay
                && PreviousDelay > DefaultDelay
                && Delay > PreviousDelay
                && (Delay - PreviousDelay).Duration() > SignificantDelayThreshold;
        }
        private void UpdateRecentPackets(IPStatus state)
        {
            RecentPackets.Enqueue(state);
            if (RecentPackets.Count > MaxRecentPackets)
            {
                RecentPackets.Dequeue();
            }
        }
        public ICMPTestTask(CancellationTokenSource CTS, string name, string ip, int timeout, int maxRecentPackets, int significantDelayThreshold)
        {
            Name = name;
            IP = ip;
            MaxRecentPackets = maxRecentPackets;
            SignificantDelayThreshold = TimeSpan.FromMilliseconds(significantDelayThreshold);

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
                        RecentLossRateRecorded?.Invoke(this);
                    }

                    UpdateRecentPackets(State);

                    pingCounter++;
                    if (pingCounter == MaxRecentPackets)
                    {
                        RecentLossRateRecorded?.Invoke(this);
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


    class Logging : IDisposable
    {
        private bool disposedValue;

        private CancellationTokenSource CTS { get; set; }
        private StreamWriter OutputFile { get; set; }
        private ConcurrentQueue<string>? Logs { get; set; }
        private StringBuilder? Builder { get; set; }
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
        public void Log(string content)
        {
            Logs?.Enqueue($"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss}] {content}");
        }
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
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    public struct ICMPTaskConfig
    {
        private string name;
        private string ip;
        private int? timeout;
        private int? maxRecentPackets;
        private int? significantDelayThreshold;

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
        }

        private static bool IsValidIP(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }

        private static bool IsValidDomainName(string name)
        {
            return Uri.CheckHostName(name) != UriHostNameType.Unknown;
        }
    }

    internal class Program
    {
        private static CancellationTokenSource CTS { get; set; } = new();
        private static Logging Logging { get; set; } = new("Pings.log");

        static void Main(string[] args)
        {
            string version = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? throw new Exception("运行环境异常！")).FileVersion?[..^2] ?? throw new Exception("程序文件异常！");
            Console.Title = $"Pings {version}";

            string configPath = Path.GetFullPath(args.Length > 0 ? args[0] : "config.txt");
            if (!File.Exists(configPath))
            {
                AnsiConsole.WriteLine($"配置文件 {configPath} 不存在。请创建配置文件后启动！");
                if (!AnsiConsole.Confirm($"是否创建默认配置文件？")) return;
                File.WriteAllText(configPath, "本机 127.0.0.1");
            }
            try
            {
                ICMPMonitor monitor = new(CTS, Logging);
                string[] configLines = File.ReadAllLines(configPath).Select(line => line.Trim()).Where(line => line.Split(' ').Length > 1).ToArray();
                foreach (var line in configLines.Select(line => new ICMPTaskConfig(line.Split(' '))))
                {
                    monitor.AddHost(new(CTS, line));
                }
                AnsiConsole.Clear();
                AnsiConsole.WriteLine();
                AnsiConsole.Live(monitor.TasksTable).StartAsync(async ctx =>
                {
                    while (!CTS.Token.IsCancellationRequested)
                    {
                        monitor.TasksTable.Title = new TableTitle($"{DateTime.Now:yyyy年MM月dd日 HH:mm:ss} 网络监测");
                        ctx.Refresh();
                        await Task.Delay(TimeSpan.FromSeconds(1), CTS.Token);
                    }
                });
                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        CTS.Cancel();
                        break;
                    }
                    if (key.Key == ConsoleKey.L)
                    {
                        foreach (var task in monitor.Tasks.FindAll(i => !i.IsWarning))
                        {
                            task.LastLog = $"丢包率 {task.RecentLossRate:F2}%";
                        }
                    }
                    if (key.Key == ConsoleKey.C)
                    {
                        foreach (var task in monitor.Tasks.FindAll(i => i.IsWarning))
                        {
                            task.Warnings.Dequeue();
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
                AnsiConsole.Write("按任意键退出...");
                Console.ReadKey(true);
                return;
            }

        }
    }
}