#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
#endif
using Spectre.Console;
using System.Net.NetworkInformation;

namespace Pings
{
    internal partial class Program
    {
#if WINDOWS
        internal static partial class ConsoleShutdown
        {
            private delegate bool HandlerRoutine(int ctrlType);

            private static HandlerRoutine? _handler;

            public static void Register(CancellationTokenSource cts)
            {
                _handler = type =>
                {
                    cts.Cancel();
                    return true;
                };

                SetConsoleCtrlHandler(_handler, true);
            }

            [LibraryImport("Kernel32")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool SetConsoleCtrlHandler(HandlerRoutine handler, [MarshalAs(UnmanagedType.Bool)] bool add);
        }
#endif
        /// <summary>全局取消令牌源</summary>
        private static CancellationTokenSource CTS { get; set; } = new();

        /// <summary>全局日志记录器</summary>
        private static Logging Logging { get; set; } = new("Pings.log");

        static void Main(string[] args)
        {
            RegisterShutdownEvents();

#if WINDOWS
            string version = FileVersionInfo
                .GetVersionInfo(Environment.ProcessPath ?? throw new Exception("运行环境异常！"))
                .FileVersion?[..^2] ?? throw new Exception("程序文件异常！");
            Console.Title = $"Pings {version}";
#endif
            string configPath = Path.GetFullPath(args.Length > 0 ? args[0] : "config.json");

            if (!File.Exists(configPath))
            {
                string oldConfigPath = Path.ChangeExtension(configPath, ".txt");

                if (File.Exists(oldConfigPath))
                {
                    AnsiConsole.WriteLine($"检测到旧版配置文件 {oldConfigPath}，正在转换为JSON格式...");

                    try
                    {
                        ConvertOldConfigToJson(oldConfigPath, configPath);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteLine($"配置升级失败: {ex.Message}");
                        WaitExit();
                        return;
                    }
                }
                else if (AnsiConsole.Confirm($"JSON配置文件 {configPath} 不存在，是否创建默认配置文件？"))
                {
                    ConfigLoader.CreateDefaultConfig(configPath);
                    AnsiConsole.WriteLine($"已创建默认配置文件: {configPath}");
                }
                else
                {
                    AnsiConsole.WriteLine("未找到配置文件，程序退出。");
                    return;
                }
            }

            try
            {
                AppConfig appConfig = ConfigLoader.LoadConfig(configPath);

                var (IsValid, ErrorMessage) = ConfigLoader.ValidateConfig(appConfig);
                if (!IsValid)
                {
                    AnsiConsole.WriteLine($"配置文件验证失败: {ErrorMessage}");
                    WaitExit();
                    return;
                }

                if (appConfig.Logging.LogFilePath != "Pings.log")
                    Logging = new Logging(appConfig.Logging.LogFilePath);

                ICMPMonitor monitor = new(CTS, Logging, appConfig.Notifications);

                foreach (var taskConfig in appConfig.Tasks)
                    monitor.AddHost(new(CTS, ConfigLoader.ToICMPTaskConfig(taskConfig)));

                AnsiConsole.Clear();
                AnsiConsole.WriteLine();

                Task displayTask = StartLiveTable(monitor);

                KeyboardLoop(monitor);

                try
                {
                    displayTask.Wait();
                }
                catch { }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception e)
            {
                AnsiConsole.WriteLine(e.ToString());
                WaitExit();
            }
        }
        private static void RegisterShutdownEvents()
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                CTS.Cancel();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                CTS.Cancel();
            };
        }

        private static Task StartLiveTable(ICMPMonitor monitor)
        {
            return AnsiConsole.Live(monitor.TasksTable).StartAsync(async ctx =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    monitor.TasksTable.Title =
                        new TableTitle($"{DateTime.Now:yyyy年MM月dd日 HH:mm:ss} 网络监测");

                    ctx.Refresh();

                    try
                    {
                        await Task.Delay(1000, CTS.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            });
        }

        private static void KeyboardLoop(ICMPMonitor monitor)
        {
            while (!CTS.IsCancellationRequested)
            {
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Q)
                {
                    CTS.Cancel();
                    break;
                }

                if (key.Key == ConsoleKey.C)
                {
                    foreach (var task in monitor.Tasks.FindAll(i => i.IsWarning))
                        _ = task.Warnings.DequeueAsync();
                }
#if DEBUG
                if (key.Key == ConsoleKey.T)
                {
                    // 随机选择一个任务并将其状态设置为IPStatus.Unknown
                    if (monitor.Tasks.Count > 0)
                    {
                        Random random = new();
                        int randomIndex = random.Next(0, monitor.Tasks.Count);
                        var randomTask = monitor.Tasks[randomIndex];

                        // 设置状态为IPStatus.Unknown
                        randomTask.State = IPStatus.Unknown;
                    }
                }
#endif
            }
        }

        private static void ConvertOldConfigToJson(string oldConfigPath, string newConfigPath)
        {
            string[] configLines = [.. File.ReadAllLines(oldConfigPath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && line.Split(' ').Length > 1)];

            if (configLines.Length == 0)
                throw new InvalidOperationException("旧版配置文件为空或格式错误");

            var appConfig = new AppConfig { Tasks = [] };

            foreach (var line in configLines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2)
                {
                    appConfig.Tasks.Add(new ICMPTaskConfigJson
                    {
                        Name = parts[0],
                        IP = parts[1],
                        Timeout = Parse(parts, 2, 1000),
                        MaxRecentPackets = Parse(parts, 3, 255),
                        SignificantDelayThreshold = Parse(parts, 4, 20),
                        PacketLossDuration = ParseDouble(parts, 5, 30.0),
                        PacketLossCount = Parse(parts, 6, 5)
                    });
                }
            }

            ConfigLoader.SaveConfig(appConfig, newConfigPath);
            AnsiConsole.WriteLine($"成功转换配置文件: {newConfigPath}");
        }

        private static int Parse(string[] parts, int index, int defaultValue)
            => parts.Length > index && int.TryParse(parts[index], out int v) ? v : defaultValue;

        private static double ParseDouble(string[] parts, int index, double defaultValue)
            => parts.Length > index && double.TryParse(parts[index], out double v) ? v : defaultValue;

        private static void WaitExit()
        {
            AnsiConsole.WriteLine("按任意键退出...");
            Console.ReadKey(true);
        }
    }
}
