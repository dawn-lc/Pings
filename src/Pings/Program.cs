using System.Runtime.InteropServices;
using System.Text;
using Pings.Cli;
using Pings.Configuration;
using Pings.Diagnostics;
#if WINDOWS
using Pings.Interop;
#endif
using Pings.Monitoring;
using Pings.Notifications;
using Pings.Ui;
using Spectre.Console;

namespace Pings
{
    /// <summary>
    /// 程序入口：只负责「参数解析 → 配置准备 → 装配服务 → 选择运行模式」。
    /// 具体职责分散在 Cli / Configuration / Monitoring / Notifications / Ui 下。
    /// </summary>
    internal static class Program
    {
        private static Logging? _logging;

        private static async Task<int> Main(string[] args)
        {
            CliOptions options = CliOptions.Parse(args, out string? parseError);

            if (parseError is not null)
            {
                Console.Error.WriteLine($"参数错误: {parseError}");
                Console.Error.WriteLine();
                Console.Error.WriteLine(CliOptions.Usage);
                return ExitCodes.InvalidArguments;
            }

            if (options.ShowHelp)
            {
                Console.WriteLine(CliOptions.Usage);
                return ExitCodes.Success;
            }

            if (options.ShowVersion)
            {
                Console.WriteLine(AppInfo.VersionText);
                return ExitCodes.Success;
            }

            InitializeConsole(options);

            try
            {
                string configPath = ResolveConfigPath(options.ConfigPath);
                EnsureConfigExists(configPath);

                AppConfig config = ConfigLoader.LoadConfig(configPath);
                ConfigValidationResult validation = ConfigValidator.Validate(config);

                if (!validation.IsValid)
                {
                    Console.Error.WriteLine($"配置文件验证失败 ({configPath}):");
                    Console.Error.WriteLine(ConfigValidator.Describe(validation));
                    return ExitCodes.Configuration;
                }

                foreach (string warning in validation.Warnings)
                    Console.WriteLine($"! {warning}");

                // 相对路径的日志文件应与配置文件同目录，避免“双击启动后日志写到别处”。
                if (!Path.IsPathRooted(config.Logging.LogFilePath))
                {
                    string configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
                    config.Logging.LogFilePath = Path.Combine(configDirectory, config.Logging.LogFilePath);
                }

                _logging = new Logging(config.Logging);
                _logging.Log($"{AppInfo.VersionText} 开始运行 (配置文件: {configPath})");

                using CancellationTokenSource cts = new();

                // 静态事件注册必须在方法返回前注销：否则之后的 Ctrl+C 会去 Cancel
                // 已经 Dispose 的 CTS，在信号处理线程里抛 ObjectDisposedException（进程崩溃）。
                using IDisposable shutdown = RegisterShutdownHandlers(cts);

                using NotificationService? notifier = CreateNotifier(config.Notifications);
                await using IcmpMonitor monitor = new(_logging, notifier, config.Global);
                foreach (IcmpTaskConfigJson task in config.Tasks)
                    monitor.Add(IcmpTaskOptions.FromJson(task));

                return options.Once
                    ? await RunOnceAsync(monitor, cts.Token).ConfigureAwait(false)
                    : await RunLiveAsync(monitor, config.Global, cts).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                return ReportFatal(ex, options);
            }
            finally
            {
                _logging?.Log("结束运行");
                _logging?.Dispose();
                _logging = null;
            }
        }

        /// <summary>--once：每个目标探测一次，输出静态表格后退出。</summary>
        private static async Task<int> RunOnceAsync(IcmpMonitor monitor, CancellationToken token)
        {
            await monitor.ProbeAllOnceAsync(token).ConfigureAwait(false);

            MonitorView view = new(monitor, TimeSpan.FromSeconds(1));
            view.RenderOnce();

            bool anyUnreachable = monitor.Snapshots().Any(snapshot => snapshot.Category != IcmpFaultCategory.None);
            return anyUnreachable ? ExitCodes.Unreachable : ExitCodes.Success;
        }

        /// <summary>实时模式：后台任务持续探测，前台循环负责渲染与按键。</summary>
        private static async Task<int> RunLiveAsync(IcmpMonitor monitor, GlobalConfig global, CancellationTokenSource cts)
        {
            TimeSpan refreshInterval = TimeSpan.FromMilliseconds(Math.Clamp(global.UiRefreshInterval, 200, 60_000));
            MonitorView view = new(monitor, refreshInterval, CreateTerminalPreparation());

            // 先抑制控制台日志，再启动探测，避免首帧之前打印日志破坏 Live 布局。
            if (_logging is not null)
                _logging.SuppressConsole = true;

            monitor.StartAll(cts.Token);

            try
            {
                Task render = view.RunLiveAsync(cts.Token);

                try
                {
                    await HandleConsoleKeysAsync(monitor, global, cts).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Ctrl+C / SIGTERM / 控制台关闭：继续走统一的收尾流程
                }
                finally
                {
                    cts.Cancel();
                }

                await render.ConfigureAwait(false);
            }
            finally
            {
                if (_logging is not null)
                    _logging.SuppressConsole = false;
            }

            return ExitCodes.Success;
        }

        /// <summary>
        /// 处理 C/Q 快捷键与定时自动确认。
        /// 输入被重定向（服务/管道运行）时不做任何按键读取，只等待取消。
        /// </summary>
        private static async Task HandleConsoleKeysAsync(IcmpMonitor monitor, GlobalConfig global, CancellationTokenSource cts)
        {
            CancellationToken token = cts.Token;

            if (Console.IsInputRedirected)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return;
            }

            DateTimeOffset lastAutoConfirm = DateTimeOffset.Now;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    while (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                        if (key.Key == ConsoleKey.Q)
                        {
                            cts.Cancel();
                            return;
                        }

                        if (key.Key == ConsoleKey.C)
                            monitor.ConfirmAllWarnings();
                    }
                }
                catch (InvalidOperationException)
                {
                    // 输入流在运行期被关闭时直接进入等待取消，避免忙轮询。
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    return;
                }

                if (global.AutoConfirmWarningInterval > 0
                    && (DateTimeOffset.Now - lastAutoConfirm).TotalSeconds >= global.AutoConfirmWarningInterval)
                {
                    monitor.ConfirmAllWarnings();
                    lastAutoConfirm = DateTimeOffset.Now;
                }

                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }

        private static void InitializeConsole(CliOptions options)
        {
            // 旧版 config.txt 可能是 GBK，注册代码页提供程序用于编码探测。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);

            AnsiConsole.Profile.Capabilities.Unicode = true;

            // 只关颜色，不要关 ANSI：备用缓冲区、光标控制、清屏都依赖 ANSI，
            // 关掉 ANSI 会让 --no-color 连带失去这些能力。
            if (options.NoColor)
                AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;

#if WINDOWS
            Console.Title = AppInfo.VersionText;
            try
            {
                // ANSI/VT 处理与备用缓冲区由 Spectre.Console 负责，这里只处理字体与代码页。
                WindowsConsole.EnableUtf8Support();

                // 东亚语言的默认控制台字体（简体中文是宋体）会把 ± → … · 这类「歧义宽度」
                // 字符画成两格，与 Spectre 的一格计算冲突；详见 EnsureNarrowAmbiguousWidth。
                WindowsConsole.EnsureNarrowAmbiguousWidth();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 某些宿主（Windows Terminal / 重定向）不允许改代码页或字体，忽略即可。
            }
#endif
        }

        /// <summary>
        /// 实时模式进入备用缓冲区之后要做的终端准备。返回空操作表示无需处理。
        /// </summary>
        /// <remarks>
        /// 必须放在<strong>进入备用缓冲区之后</strong>再执行：conhost 的备用缓冲区有自己的一份字体
        /// （即控制台默认字体），切换缓冲区时窗口字体跟着变回去。实测先设 Consolas 再进备用缓冲区，
        /// 进去后字体又变回新宋体，<c>±</c> 重新变成两格——这正是双击启动时表格错位的直接原因。
        /// </remarks>
        private static Action? CreateTerminalPreparation()
        {
#if WINDOWS
            return () => WindowsConsole.EnsureNarrowAmbiguousWidth();
#else
            return null;
#endif
        }

        /// <summary>
        /// 注册 Ctrl+C / SIGTERM / 控制台关闭的统一收尾。
        /// 返回的 <see cref="IDisposable" /> 负责注销这些静态注册。
        /// </summary>
        private static IDisposable RegisterShutdownHandlers(CancellationTokenSource cts)
        {
            ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
            {
                e.Cancel = true;
                Cancel(cts);
            };

            EventHandler onProcessExit = (_, _) => _logging?.Dispose();

            Console.CancelKeyPress += onCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += onProcessExit;

            // systemd / kill 默认发 SIGTERM，捕获后走与 Ctrl+C 相同的收尾路径。
            PosixSignalRegistration? sigterm = null;
            if (!OperatingSystem.IsWindows())
            {
                sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                {
                    context.Cancel = true;
                    Cancel(cts);
                });
            }

            return new ActionOnDispose(() =>
            {
                Console.CancelKeyPress -= onCancelKeyPress;
                AppDomain.CurrentDomain.ProcessExit -= onProcessExit;
                sigterm?.Dispose();
            });
        }

        /// <summary>
        /// 取消令牌。信号处理器可能晚于 <c>using</c> 释放到达，
        /// 这里吞掉已释放异常——退出阶段再崩一次毫无意义。
        /// </summary>
        private static void Cancel(CancellationTokenSource cts)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>把清理回调包装成 <see cref="IDisposable" />。</summary>
        private sealed class ActionOnDispose(Action action) : IDisposable
        {
            private Action? _action = action;

            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }

        private static NotificationService? CreateNotifier(NotificationsConfig config)
        {
            if (!config.Webhook.Enabled && !config.Email.Enabled)
                return null;

            if (_logging is not null)
                _logging.Log("通知功能已启用");

            return new NotificationService(_logging!, config);
        }

        /// <summary>
        /// 解析配置路径：显式参数优先；否则先找当前目录，再找程序所在目录（便于双击启动）。
        /// </summary>
        private static string ResolveConfigPath(string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return Path.GetFullPath(configuredPath);

            string currentDirectory = Path.GetFullPath("config.json");
            if (File.Exists(currentDirectory))
                return currentDirectory;

            string besideExecutable = Path.Combine(AppContext.BaseDirectory, "config.json");
            return File.Exists(besideExecutable) ? besideExecutable : currentDirectory;
        }

        private static void EnsureConfigExists(string configPath)
        {
            if (File.Exists(configPath))
                return;

            string legacyPath = Path.ChangeExtension(configPath, ".txt");

            if (File.Exists(legacyPath))
            {
                Console.WriteLine($"检测到旧版配置文件 {legacyPath}，正在转换为 JSON 格式...");
                LegacyConfigConverter.ConvertAndSave(legacyPath, configPath);
                Console.WriteLine($"已生成 {configPath}");
                return;
            }

            if (!Console.IsInputRedirected
                && AnsiConsole.Confirm($"配置文件 {configPath} 不存在，是否创建默认配置？"))
            {
                ConfigLoader.CreateDefaultConfig(configPath);
                Console.WriteLine($"已创建默认配置文件: {configPath}");
                return;
            }

            throw new InvalidOperationException($"未找到配置文件: {configPath}");
        }

        private static int ReportFatal(Exception exception, CliOptions options)
        {
            _logging?.Log($"发生未处理异常: {exception}");

            Console.Error.WriteLine($"错误: {exception.Message}");

            if (options.Verbose || _logging is null)
                Console.Error.WriteLine(exception.ToString());

            if (!options.NoPause
                && Environment.UserInteractive
                && !Console.IsInputRedirected
                && !Console.IsOutputRedirected)
            {
                Console.Error.WriteLine("按任意键退出...");
                try
                {
                    Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException)
                {
                }
            }

            return ExitCodes.RuntimeError;
        }
    }
}
