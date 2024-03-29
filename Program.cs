﻿using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
                IPStatus.Success => "请求成功",
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
        public Table Tasks { get; set; } 

        public ICMPMonitor(CancellationTokenSource cancellationTokenSource, Logging? logging)
        {
            TaskMap = [];
            Tasks = new();
            CancellationTokenSource = cancellationTokenSource;
            Logging = logging;
            Tasks.AddColumns("名称", "IP/域名", "状态", "延迟");
        }
        public void AddHost(string name, string ip, int timeout)
        {
            ICMPTestTask task = new(name, ip, timeout, CancellationTokenSource);
            TaskMap.Add(task.IP, Tasks.Rows.Add(new List<IRenderable> { new Text(task.Name), new Text(task.IP), new Text(task.State.ToChineseString()), new Text($"{(int)task.Delay.TotalMilliseconds}ms") }));
            task.DelayExceptionOccurred += (task) => Logging?.Log($"{task.Name}({task.IP}) 延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(int)task.Delay.TotalMilliseconds}ms");
            task.StatusChanged += (task) =>
            {
                Tasks.Rows.Update(TaskMap[task.IP], 2, new Text(task.State.ToChineseString()));
                Logging?.Log($"{task.Name}({task.IP}) {task.State.ToChineseString()} {(task.State == IPStatus.Success ? $"{(int)task.Delay.TotalMilliseconds}ms" : null)}");
            };
            task.DelayChanged += (task) =>
            {
                Tasks.Rows.Update(TaskMap[task.IP], 3, new Text($"{(int)task.Delay.TotalMilliseconds}ms"));
            };
        }
    }
    class ICMPTestTask
    {
        public event Action<ICMPTestTask>? StatusChanged;
        public event Action<ICMPTestTask>? DelayChanged;
        public event Action<ICMPTestTask>? DelayExceptionOccurred;
        public string Name { get; }
        public string IP { get; }
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
                }
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
                    if (IsSignificantDelayChange()) DelayExceptionOccurred?.Invoke(this);
                }
            }
        }
        private bool IsSignificantDelayChange()
        {
            const double SIGNIFICANT_CHANGE_THRESHOLD = 0.1;
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
        static Dictionary<string, string> GetConfig(string path)
        {
            string configPath = Path.GetFullPath(path);
            if (!File.Exists(configPath))
            {
                Logging.Log($"配置文件 {configPath} 不存在。");
                AnsiConsole.WriteLine($"配置文件 {configPath} 不存在。");
                Environment.Exit(1);
            }
            Dictionary<string, string> config = [];
            foreach (string line in File.ReadAllLines(configPath))
            {
                var split = line.Split(' ');
                if (split.Length < 2)
                {
                    Logging.Log($"配置文件格式错误：{line}");
                    AnsiConsole.WriteLine($"配置文件格式错误：{line}");
                    continue;
                }
                var (name, ip) = (split[0], split[1]);
                if (!IsValidIP(ip) && !IsValidDomainName(ip))
                {
                    Logging.Log($"无效配置：{line}");
                    AnsiConsole.WriteLine($"无效配置：{line}");
                    continue;
                }
                config[name] = ip;
            }
            if (config.Count<1)
            {
                Logging.Log($"配置文件中未找到可读取的配置");
                AnsiConsole.WriteLine($"配置文件中未找到可读取的配置");
                Environment.Exit(1);
            }
            return config;
        }
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Logging.Log("程序退出。");
                CTS.Cancel();
                Logging.Dispose();
            };

            ICMPMonitor monitor = new(CTS, Logging);
            foreach (var item in GetConfig(args.Length > 0 ? args[0] : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "config.txt" : "/etc/pings/config"))
            {
                monitor.AddHost(item.Key, item.Value, 1000);
            }

            AnsiConsole.Clear();
            AnsiConsole.Live(monitor.Tasks).Start((ldc) =>
            {
                while (!CTS.IsCancellationRequested)
                {
                    ldc.Refresh();
                    Thread.Sleep(100);
                }
            });
        }
    }
}