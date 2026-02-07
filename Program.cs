#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
#endif
using Spectre.Console;
using System.Net.NetworkInformation;
using System.Text;

namespace Pings
{
    internal partial class Program
    {
        private static CancellationTokenSource CTS { get; set; } = new();

        private static Logging Logging { get; set; } = new("pings.log");
        private static NotificationService? Notifier { get; set; }

#if WINDOWS
        internal static partial class WindowsAPI
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct CONSOLE_FONT_INFO_EX
            {
                public uint cbSize;
                public uint nFont;

                public short dwFontSizeX;
                public short dwFontSizeY;

                public int FontFamily;
                public int FontWeight;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string FaceName;
            }


            [StructLayout(LayoutKind.Sequential)]
            public struct COORD
            {
                public short X;
                public short Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct WINDOW_BUFFER_SIZE_RECORD
            {
                public COORD dwSize;
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct INPUT_RECORD
            {
                [FieldOffset(0)]
                public ushort EventType;

                [FieldOffset(4)]
                public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
            }

            [LibraryImport("kernel32.dll", SetLastError = true)]
            public static partial IntPtr GetStdHandle(int nStdHandle);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static partial bool SetConsoleOutputCP(uint wCodePageID);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static partial bool SetConsoleCP(uint wCodePageID);

            public delegate bool HandlerRoutine(int ctrlType);
            [LibraryImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static partial bool SetConsoleCtrlHandler(HandlerRoutine handler, [MarshalAs(UnmanagedType.Bool)] bool add);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetCurrentConsoleFontEx(IntPtr consoleOutput, [MarshalAs(UnmanagedType.Bool)] bool maximumWindow, ref CONSOLE_FONT_INFO_EX consoleFontEx);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static partial bool GetConsoleMode(
               IntPtr hConsoleHandle,
               out uint lpMode);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static partial bool SetConsoleMode(
                IntPtr hConsoleHandle,
                uint dwMode);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static partial bool ReadConsoleInputW(
                IntPtr hConsoleInput,
                [Out] INPUT_RECORD[] buffer,
                uint length,
                out uint eventsRead);
        }

        internal static partial class WindowsConsoleCompatible
        {
            private const int STD_OUTPUT_HANDLE = -11;
            public static void ForceSetConsoleFont(string fontName, short fontSizeY = 16)
            {
                var handle = WindowsAPI.GetStdHandle(STD_OUTPUT_HANDLE);

                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return;

                var info = new WindowsAPI.CONSOLE_FONT_INFO_EX
                {
                    cbSize = (uint)Marshal.SizeOf<WindowsAPI.CONSOLE_FONT_INFO_EX>(),
                    FaceName = fontName,
                    dwFontSizeX = 0,
                    dwFontSizeY = fontSizeY,
                    FontFamily = 54,
                    FontWeight = 400
                };

                WindowsAPI.SetCurrentConsoleFontEx(handle, false, ref info);
            }
            public static void EnableUTF8Support()
            {
                const uint CP_UTF8 = 65001;
                if (!WindowsAPI.SetConsoleOutputCP(CP_UTF8))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法设置控制台输出代码页为UTF-8");
                }
                if (!WindowsAPI.SetConsoleCP(CP_UTF8))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法设置控制台输入代码页为UTF-8");
                }
            }
            public static void RegisterConsoleShutdown(WindowsAPI.HandlerRoutine handler)
            {
                WindowsAPI.SetConsoleCtrlHandler(handler, true);
            }
        }


        internal static partial class ConsoleResizeWatcher
        {
            private const int STD_INPUT_HANDLE = -10;

            private const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;

            private const uint ENABLE_WINDOW_INPUT = 0x0008;
            private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
            private const uint ENABLE_QUICK_EDIT = 0x0040;


            private static Task? worker;

            private static int lastWidth;
            private static int lastHeight;

            public static bool IsRunning => worker != null && !worker.IsCompleted;

            public static event Action<int, int>? Resized;

            public static void Start(CancellationToken token)
            {
                if (IsRunning)
                    return;

                worker = Task.Run(() =>
                {
                    var handle = WindowsAPI.GetStdHandle(STD_INPUT_HANDLE);

                    if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                        return;

                    if (WindowsAPI.GetConsoleMode(handle, out uint mode))
                    {
                        uint newMode =
                            mode
                            | ENABLE_WINDOW_INPUT
                            | ENABLE_EXTENDED_FLAGS;

                        newMode &= ~ENABLE_QUICK_EDIT;

                        WindowsAPI.SetConsoleMode(handle, newMode);
                    }

                    lastWidth = Console.WindowWidth;
                    lastHeight = Console.WindowHeight;

                    var records = new WindowsAPI.INPUT_RECORD[1];

                    while (!token.IsCancellationRequested)
                    {
                        if (!WindowsAPI.ReadConsoleInputW(handle, records, 1, out uint read))
                        {
                            continue;
                        }

                        if (read == 0)
                            continue;

                        ref var record = ref records[0];

                        if (record.EventType != WINDOW_BUFFER_SIZE_EVENT)
                            continue;

                        int w = Console.WindowWidth;
                        int h = Console.WindowHeight;

                        if (w == lastWidth && h == lastHeight)
                            continue;

                        lastWidth = w;
                        lastHeight = h;

                        Resized?.Invoke(w, h);
                    }

                }, token);
            }
        }
#endif

        public static void Exit(int code = 0, string msg = "结束运行")
        {
            Logging.Log(msg);
            Environment.Exit(code);
        }
        private static void InitializeConsoleRuntime()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
#if WINDOWS
            WindowsConsoleCompatible.EnableUTF8Support();
            WindowsConsoleCompatible.ForceSetConsoleFont("Consolas", 18);
            AnsiConsole.Profile.Capabilities.Unicode = true;
            ConsoleResizeWatcher.Resized += (w, h) =>
            {
                if (w < 64) Exit(1, "控制台窗口宽度过小！");
            };
            ConsoleResizeWatcher.Start(CTS.Token);
#endif
        }
        private static void RegisterShutdownEvents()
        {
#if WINDOWS
            WindowsConsoleCompatible.RegisterConsoleShutdown(ctrlType =>
            {
                Exit();
                return false;
            });
#endif
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                Notifier?.Dispose();
                Logging.Dispose();
                CTS.Cancel();
            };
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Exit();
            };
        }

        static async Task Main(string[] args)
        {
            try
            {
                Logging.Log($"开始运行");
                InitializeConsoleRuntime();
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
                            throw new Exception($"配置升级失败: {ex.Message}");
                        }
                    }
                    else if (AnsiConsole.Confirm($"JSON配置文件 {configPath} 不存在，是否创建默认配置文件？"))
                    {
                        ConfigLoader.CreateDefaultConfig(configPath);
                        AnsiConsole.WriteLine($"已创建默认配置文件: {configPath}");
                    }
                    else
                    {
                        throw new Exception("未找到配置文件。");
                    }
                }

                AppConfig appConfig = ConfigLoader.LoadConfig(configPath);

                var (IsValid, ErrorMessage) = ConfigLoader.ValidateConfig(appConfig);
                if (!IsValid)
                {
                    throw new Exception($"配置文件验证失败: {ErrorMessage}");
                }

                if (appConfig.Logging.LogFilePath != "Pings.log") Logging = new Logging(appConfig.Logging.LogFilePath);

                if (appConfig.Notifications.Webhook.Enabled || appConfig.Notifications.Email.Enabled)
                {
                    Notifier = new NotificationService(Logging, appConfig.Notifications);
                    Logging.Log("通知功能已启用");
                }

                ICMPMonitor monitor = new(Logging, Notifier);

                foreach (var taskConfig in appConfig.Tasks)
                {
                    monitor.AddHost(new(CTS, ConfigLoader.ToICMPTaskConfig(taskConfig)));
                }

                Task displayTask = StartLiveTable(monitor);

                while (!CTS.IsCancellationRequested)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        Exit();
                        break;
                    }

                    if (key.Key == ConsoleKey.C)
                    {
                        foreach (var task in monitor.Tasks.FindAll(i => i.Warnings.Count > 0))
                            _ = task.Warnings.DequeueAsync();
                    }
#if DEBUG
                    if (key.Key == ConsoleKey.T)
                    {
                        foreach (var task in monitor.Tasks)
                        {
                            task.State = IPStatus.Unknown;
                            //await task.Warnings.EnqueueAsync($"Test Warning! {task.Warnings.Count+1}");
                        }
                    }
#endif
                }

                try
                {
                    displayTask.Wait();
                }
                catch { }
            }
            catch (TaskCanceledException)
            {
                Exit();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Console.WriteLine("按任意键退出...");
                Console.ReadKey(true);
                Exit(1, e.ToString());
            }
        }

        private static Task StartLiveTable(ICMPMonitor monitor)
        {
            return AnsiConsole.Live(monitor.TasksTable).StartAsync(async ctx =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    monitor.TasksTable.Title = new TableTitle($"{DateTime.Now:yyyy年MM月dd日 HH:mm:ss} 网络监测");

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
    }
}