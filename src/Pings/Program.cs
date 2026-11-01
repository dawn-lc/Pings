using System.Runtime.InteropServices;
using System.Text;
using Pings.Cli;
using Pings.Configuration;
using Pings.Diagnostics;
#if WINDOWS
using Pings.Interop;
#endif
using Pings.Localization;
using Pings.Monitoring;
using Pings.Notifications;
using Pings.Properties;
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
            // 语言必须在完整解析**之前**确定：否则「未知参数」这类报错只能用默认语言输出。
            UiLanguage.Apply(CliOptions.FindLanguage(args));

            CliOptions options = CliOptions.Parse(args, out string? parseError);

            if (parseError is not null)
            {
                Console.Error.WriteLine(string.Format(Resources.ProgramArgumentError, parseError));
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
                EnsureConfigExists(configPath, options);

                AppConfig config = ConfigLoader.LoadConfig(configPath);

                // 命令行 --lang 优先于配置文件；两者都没写时保持启动时探测到的语言。
                if (options.Language is not null || !string.IsNullOrWhiteSpace(config.Global.Language))
                    UiLanguage.Apply(options.Language ?? config.Global.Language);

                // 相对路径（日志文件、UDP 负载文件）都与配置文件同目录，
                // 避免「双击启动后工作目录变了，文件却找不到」。
                string configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

                ConfigValidationResult validation = ConfigValidator.Validate(config, configDirectory);

                if (!validation.IsValid)
                {
                    Console.Error.WriteLine(string.Format(Resources.ProgramConfigValidationFailed, configPath));
                    Console.Error.WriteLine(ConfigValidator.Describe(validation));
                    return ExitCodes.Configuration;
                }

                foreach (string warning in validation.Warnings)
                    Console.WriteLine($"! {warning}");

                if (!Path.IsPathRooted(config.Logging.LogFilePath))
                    config.Logging.LogFilePath = Path.Combine(configDirectory, config.Logging.LogFilePath);

                _logging = new Logging(config.Logging);
                _logging.Log(string.Format(Resources.ProgramStartup, AppInfo.VersionText, configPath));

                using CancellationTokenSource cts = new();

                // 静态事件注册必须在方法返回前注销：否则之后的 Ctrl+C 会去 Cancel
                // 已经 Dispose 的 CTS，在信号处理线程里抛 ObjectDisposedException（进程崩溃）。
                using IDisposable shutdown = RegisterShutdownHandlers(cts);

                using NotificationService? notifier = CreateNotifier(config.Notifications);
                await using ProbeMonitor monitor = new(_logging, notifier, config.Global);
                foreach (ProbeTaskConfigJson task in config.Tasks)
                    monitor.Add(ProbeTaskOptions.FromJson(task, configDirectory));

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
                _logging?.Log(Resources.ProgramShutdown);
                _logging?.Dispose();
                _logging = null;
            }
        }

        /// <summary>--once：每个目标探测一次，输出静态表格后退出。</summary>
        private static async Task<int> RunOnceAsync(ProbeMonitor monitor, CancellationToken token)
        {
            await monitor.ProbeAllOnceAsync(token).ConfigureAwait(false);

            MonitorView view = new(monitor, TimeSpan.FromSeconds(1));
            view.RenderOnce();

            bool anyUnreachable = monitor.Snapshots().Any(snapshot => snapshot.Category != ProbeFaultCategory.None);
            return anyUnreachable ? ExitCodes.Unreachable : ExitCodes.Success;
        }

        /// <summary>实时模式：后台任务持续探测，前台循环负责渲染与按键。</summary>
        private static async Task<int> RunLiveAsync(ProbeMonitor monitor, GlobalConfig global, CancellationTokenSource cts)
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
        private static async Task HandleConsoleKeysAsync(ProbeMonitor monitor, GlobalConfig global, CancellationTokenSource cts)
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
                _logging.Log(Resources.ProgramNotificationsEnabled);

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

        /// <summary>
        /// 确保配置文件存在：旧版 .txt 自动迁移；都不存在时询问是否创建默认配置。
        /// </summary>
        /// <remarks>
        /// <c>--once</c> 与 <c>--no-pause</c> 下**不询问**，直接报错退出（见
        /// <see cref="CliOptions.IsNonInteractive" />）。这两个开关的共同承诺是「不需要人盯」，
        /// 而等待 stdin 恰好会把它变成挂起：脚本里把配置路径写错一个字母，
        /// 进程就会静静停在那里直到超时被杀。
        /// 非交互环境（stdin 被重定向）本来就读不到按键，同样跳过。
        /// </remarks>
        private static void EnsureConfigExists(string configPath, CliOptions options)
        {
            if (File.Exists(configPath))
                return;

            string legacyPath = Path.ChangeExtension(configPath, ".txt");

            if (File.Exists(legacyPath))
            {
                Console.WriteLine(string.Format(Resources.ProgramLegacyDetected, legacyPath));
                LegacyConfigConverter.ConvertAndSave(legacyPath, configPath);
                Console.WriteLine(string.Format(Resources.ProgramLegacyGenerated, configPath));
                return;
            }

            bool canPrompt = !options.IsNonInteractive && !Console.IsInputRedirected;

            if (canPrompt
                && AnsiConsole.Confirm(string.Format(Resources.ProgramCreateDefaultPrompt, configPath)))
            {
                ConfigLoader.CreateDefaultConfig(configPath);
                Console.WriteLine(string.Format(Resources.ProgramDefaultCreated, configPath));
                return;
            }

            throw new InvalidOperationException(string.Format(Resources.ConfigFileNotFound, configPath));
        }

        private static int ReportFatal(Exception exception, CliOptions options)
        {
            _logging?.Log($"{Resources.ProgramUnhandledException}: {exception}");

            Console.Error.WriteLine(string.Format(Resources.ProgramError, exception.Message));

            if (options.Verbose || _logging is null)
                Console.Error.WriteLine(exception.ToString());

            if (!options.IsNonInteractive
                && Environment.UserInteractive
                && !Console.IsInputRedirected
                && !Console.IsOutputRedirected)
            {
                Console.Error.WriteLine(Resources.ProgramPressAnyKey);
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
