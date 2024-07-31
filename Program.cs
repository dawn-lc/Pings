using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
namespace Pings
{
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
            TasksTable = new() { Caption = new TableTitle("按 Q 退出/按 M 消除警告") };
            TasksTable.AddColumns("名称", "IP/域名", "状态", "延迟", "记录");
            TasksTable.Centered();
        }

        public void AddHost(string name, string ip, int timeout)
        {
            ICMPTestTask newTask = new(name, ip, timeout, CancellationTokenSource);
            TaskMap.Add(newTask.IP, TasksTable.Rows.Add([new Text(newTask.Name), new Text(newTask.IP), new Text(newTask.State.ToChineseString()), new Text($"{(newTask.Delay.TotalMilliseconds < 0 ? "∞" : (int)newTask.Delay.TotalMilliseconds)}ms"), new Text(newTask.LastLog, newTask.Warning ? new Style(Color.Yellow, Color.Red, Decoration.Bold) : null)]));
            newTask.LastLogChanged += (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog, task.Warning ? new Style(Color.Yellow, Color.Red, Decoration.Bold) : null));
            };
            newTask.DelayChanged += (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 3, new Text($"{(task.Delay.TotalMilliseconds < 0 ? "∞" : (int)task.Delay.TotalMilliseconds)}ms"));
            };
            newTask.WarningChanged += (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog, task.Warning ? new Style(Color.Yellow, Color.Red, Decoration.Bold) : null));
                Logging?.Log($"{task.Name}({task.IP}) {(task.Warning ? "触发" : "解除")}警告 当前状态：{task.State.ToChineseString()}");
            };
            newTask.StatusChanged += (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 2, new Text(task.State.ToChineseString()));
                Logging?.Log($"{task.Name}({task.IP}) {task.State.ToChineseString()} {(task.Delay.TotalMilliseconds < 0 ? "∞" : (int)task.Delay.TotalMilliseconds)}ms");
            };
            newTask.DelayExceptionOccurred += (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) 延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(task.Delay.TotalMilliseconds < 0 ? "∞" : (int)task.Delay.TotalMilliseconds)}ms");
            };
            Tasks.Add(newTask);
        }
    }


    class ICMPTestTask
    {

        public event Action<ICMPTestTask>? WarningChanged;
        public event Action<ICMPTestTask>? LastLogChanged;
        public event Action<ICMPTestTask>? StatusChanged;
        public event Action<ICMPTestTask>? DelayChanged;
        public event Action<ICMPTestTask>? DelayExceptionOccurred;
        public string Name { get; }
        public string IP { get; }

        private string? lastLog;
        public string LastLog
        {
            get
            {
                return lastLog ?? State.ToChineseString();
            }
            set
            {
                if (!Warning && lastLog != value)
                {
                    lastLog = value;
                }
                LastLogChanged?.Invoke(this);
            }
        }

        private bool? warning;
        public bool Warning
        {
            get
            {
                return warning ?? false;
            }
            set
            {
                if (warning != value)
                {
                    warning = value;
                    WarningChanged?.Invoke(this);
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
                if (state != value)
                {
                    state = value;
                    StatusChanged?.Invoke(this);
                    LastLog = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss} {State.ToChineseString()}";
                }
                if (State != IPStatus.Success) Warning = true;
            }
        }
        public TimeSpan PreviousDelay { get; set; } 
        private TimeSpan delay;
        public TimeSpan Delay
        {
            get
            {
                return delay;
            }
            set
            {
                if (delay != value)
                {
                    if (value > TimeSpan.FromMilliseconds(-1)) PreviousDelay = delay;
                    delay = value;
                    DelayChanged?.Invoke(this);
                    if (IsSignificantDelayChange())
                    {
                        DelayExceptionOccurred?.Invoke(this);
                        LastLog = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss} 延迟波动 {(int)PreviousDelay.TotalMilliseconds}ms -> {(int)Delay.TotalMilliseconds}ms";
                    }
                }
            }
        }

        private bool IsSignificantDelayChange()
        {
            const double SIGNIFICANT_CHANGE_THRESHOLD = 0.3;
            return State == IPStatus.Success
                && delay > PreviousDelay
                && PreviousDelay > TimeSpan.FromMilliseconds(0)
                && delay > TimeSpan.FromMilliseconds(0)
                && (delay - PreviousDelay).Duration() > TimeSpan.FromMilliseconds(1)
                && (delay - PreviousDelay).Duration() / PreviousDelay >= SIGNIFICANT_CHANGE_THRESHOLD;
        }
        public ICMPTestTask(string name, string ip, int timeout, CancellationTokenSource CTS)
        {
            Name = name;
            IP = ip;
            PreviousDelay = TimeSpan.FromMilliseconds(-1);
            Task.Run(async () =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    using Ping ping = new();
                    try
                    {
                        PingReply reply = ping.Send(IP, timeout);
                        Delay = reply.Status == IPStatus.Success ? TimeSpan.FromMilliseconds(reply.RoundtripTime) : TimeSpan.FromMilliseconds(-1);
                        State = reply.Status;
                    }
                    catch
                    {
                        State = IPStatus.Unknown;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(timeout), CTS.Token);
                }
            }, CTS.Token);
        }
    }
    
    class Logging: IDisposable
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
                    await Task.Delay(TimeSpan.FromMilliseconds(100), CTS.Token);
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

    internal class Program
    {
        private static CancellationTokenSource CTS { get; set; } = new ();
        private static Logging Logging { get; set; } = new(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pings.log" : "/var/log/pings.log");
        static bool IsValidIP(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }
        static bool IsValidDomainName(string name)
        {
            return Uri.CheckHostName(name) != UriHostNameType.Unknown;
        }
        static void Main(string[] args)
        {
            string configPath = Path.GetFullPath(args.Length > 0 ? args[0] : "config.txt");
            if (!File.Exists(configPath))
            {
                AnsiConsole.WriteLine($"配置文件 {configPath} 不存在。请创建配置文件后启动！");
                return;
            }
            string[] configLines = File.ReadAllLines(configPath);
            ICMPMonitor monitor = new(CTS, Logging);
            foreach (var line in configLines)
            {
                var parts = line.Split(' ');
                if (parts.Length < 2)
                {
                    AnsiConsole.WriteLine($"配置文件格式错误：{line}");
                    return;
                }
                if (!IsValidIP(parts[1]) && !IsValidDomainName(parts[1]))
                {
                    AnsiConsole.WriteLine($"无效的IP地址或域名：{parts[1]}");
                    return;
                }
                monitor.AddHost(parts[0], parts[1], 1000);
            }
            AnsiConsole.Live(monitor.TasksTable).StartAsync(async ctx =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    monitor.TasksTable.Title = new TableTitle($"{DateTime.Now:yyyy年MM月dd日 HH:mm:ss} 网络侦测");
                    ctx.Refresh();
                    await Task.Delay(500, CTS.Token);
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
                if (key.Key == ConsoleKey.M)
                {
                    foreach (var task in monitor.Tasks)
                    {
                        task.Warning = false;
                    }
                }
            }
        }
    }
}