using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
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
                IPStatus.Success => "连接正常",
                IPStatus.TimedOut => "连接超时",
                IPStatus.TimeExceeded => "生存时间过期",
                IPStatus.TtlExpired => "TTL值过期",
                IPStatus.TtlReassemblyTimeExceeded => "重组超时",
                IPStatus.Unknown => "未知错误",
                IPStatus.UnrecognizedNextHeader => "下一标头无法识别",
                _ => "未知状态",
            };
        }
    }
    class ICMPMonitor
    {
        private ConcurrentQueue<string> Logs { get; set; }
        private CancellationTokenSource CTS { get; set; }
        private Dictionary<string, ICMPTestTask> Tasks { get; set; }
        public ICMPMonitor(CancellationTokenSource cancellationTokenSource)
        {
            Logs = new();
            Tasks = new();
            CTS = cancellationTokenSource;
            Task.Run(async () =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    await WriteLog();
                    await Task.Delay(TimeSpan.FromMilliseconds(100), CTS.Token);
                }
                await WriteLog();
            }, CTS.Token);
        }
        private Task WriteLog()
        {
            using StreamWriter outputFile = new(Path.GetFullPath("pings.log"), true);
            StringBuilder builder = new();
            while (Logs.TryDequeue(out string? item) && item != null)
            {
                builder.AppendLine(item);
            }
            return outputFile.WriteAsync(builder);
        }
        public void AddHost(string name, string ip, int timeout)
        {
            Tasks[ip] = new ICMPTestTask(name, ip, timeout, CTS);
            Tasks[ip].StatusChanged += (task) => Logs.Enqueue($"[{task.Time:yyyy/MM/dd hh:mm:ss}] {task.Name}({task.IP}) {task.State.ToChineseString()}");
        }
        public Table GetTable()
        {
            Table table = new() { Title = new TableTitle("Pings (按Q键退出程序)") };
            table.AddColumns("名称", "IP/域名", "状态", "延迟");
            foreach (var task in Tasks.Values)
            {
                table.AddRow(task.Name, task.IP, task.State.ToChineseString(), task.Delay == null ? "无效": $"{task.Delay?.Milliseconds}ms");
            }
            return table;
        }
    }
    class ICMPTestTask
    {
        public event Action<ICMPTestTask>? StatusChanged;
        public string Name { get; set; }
        public string IP { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;

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
                    Time = DateTime.Now;
                    StatusChanged?.Invoke(this);
                }
            }
        }
        public TimeSpan? Delay { get; set; }
        public ICMPTestTask(string name, string ip, int timeout, CancellationTokenSource CTS)
        {
            Name = name;
            IP = ip;
            Task.Run(async () =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    using Ping ping = new();
                    try
                    {
                        PingReply reply = ping.Send(IP, timeout);
                        State = reply.Status;
                        Delay = reply.Status == IPStatus.Success ? TimeSpan.FromMilliseconds(reply.RoundtripTime) : null;
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
    internal class Program
    {
        private static readonly CancellationTokenSource cts = new();
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
                AnsiConsole.WriteLine($"配置文件 {configPath} 不存在。");
                return;
            }
            string[] configLines = File.ReadAllLines(configPath);
            ICMPMonitor monitor = new(cts);
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
                monitor.AddHost(parts[0], parts[1], 500);
            }
            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    AnsiConsole.Clear();
                    AnsiConsole.Write(monitor.GetTable());
                    await Task.Delay(1000, cts.Token);
                }
            }, cts.Token);
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q)
                {
                    cts.Cancel();
                    AnsiConsole.WriteLine("正在退出...");
                    Console.CursorVisible = true;
                    break;
                }
            }
        }
    }
}