namespace Pings.Cli
{
    using System.Globalization;
    using Pings.Localization;
    using Pings.Properties;

    /// <summary>进程退出码约定。</summary>
    public static class ExitCodes
    {
        /// <summary>正常结束。</summary>
        public const int Success = 0;
        /// <summary>命令行参数错误。</summary>
        public const int InvalidArguments = 1;
        /// <summary>配置文件缺失 / 校验失败。</summary>
        public const int Configuration = 2;
        /// <summary>运行期异常。</summary>
        public const int RuntimeError = 3;
        /// <summary>--once 模式下存在不可达目标。</summary>
        public const int Unreachable = 4;

        public static string Describe(int code) => code switch
        {
            Success => Resources.ExitCodeSuccess,
            InvalidArguments => Resources.ExitCodeInvalidArguments,
            Configuration => Resources.ExitCodeConfiguration,
            RuntimeError => Resources.ExitCodeRuntimeError,
            Unreachable => Resources.ExitCodeUnreachable,
            _ => Resources.ExitCodeUnknown
        };
    }

    /// <summary>
    /// 命令行参数。
    /// </summary>
    public sealed record CliOptions(
        string? ConfigPath,
        bool Once,
        bool ShowHelp,
        bool ShowVersion,
        bool NoColor,
        bool NoPause,
        bool Verbose,
        string? Language)
    {
        /// <summary>完整用法说明（随当前语言变化）。</summary>
        public static string Usage => Resources.Usage;

        /// <summary>
        /// 本次运行不应等人输入（不提问、出错不暂停）。
        /// </summary>
        /// <remarks>
        /// <c>--once</c> 的定位就是「适合脚本/CI」：它就算没配 <c>--no-pause</c>
        /// 也不能在末尾停住等按键，否则脚本里一次配置路径笔误就会变成挂起。
        /// 两个开关的语义因此收成一个属性，避免各处各写一遍判断而漏掉一处。
        /// </remarks>
        public bool IsNonInteractive => NoPause || Once;

        /// <summary>
        /// 从参数里摘出 <c>--lang</c> 的值，不做完整解析。
        /// </summary>
        /// <remarks>
        /// 语言必须在完整解析**之前**确定：否则「未知参数」这类报错只能用默认语言输出，
        /// 而用户明明已经用 --lang 指定了语言。这里只做最小扫描，
        /// 真正的校验仍由 <see cref="Parse" /> 与配置校验负责。
        /// </remarks>
        public static string? FindLanguage(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--")
                    return null;

                if (args[i] is "--lang")
                    return args[i + 1];
            }

            return null;
        }

        public static CliOptions Parse(string[] args, out string? error)
        {
            error = null;

            string? configPath = null;
            string? language = null;
            bool once = false;
            bool showHelp = false;
            bool showVersion = false;
            bool noColor = false;
            bool noPause = false;
            bool verbose = false;
            bool positionalSeen = false;
            bool endOfOptions = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (!endOfOptions && arg == "--")
                {
                    endOfOptions = true;
                    continue;
                }

                if (!endOfOptions && arg.StartsWith('-') && arg.Length > 1)
                {
                    switch (arg)
                    {
                        case "-h":
                        case "--help":
                        case "-?":
                            showHelp = true;
                            break;

                        case "-v":
                        case "--version":
                            showVersion = true;
                            break;

                        case "--once":
                            once = true;
                            break;

                        case "--no-color":
                            noColor = true;
                            break;

                        case "--no-pause":
                            noPause = true;
                            break;

                        case "--verbose":
                            verbose = true;
                            break;

                        case "-c":
                        case "--config":
                            if (i + 1 >= args.Length)
                            {
                                error = string.Format(Resources.CliMissingOptionValue, arg);
                                return Default();
                            }

                            configPath = args[++i];
                            break;

                        case "--lang":
                            if (i + 1 >= args.Length)
                            {
                                error = string.Format(Resources.CliMissingOptionValue, arg);
                                return Default();
                            }

                            language = args[++i];

                            // 非法值必须当场报错：静默忽略会让用户以为语言已切换，
                            // 而界面其实还在用别的语言（auto 也一样要能进）。
                            if (!UiLanguage.TryResolve(language, out CultureInfo? _))
                            {
                                error = string.Format(Resources.CliInvalidLanguage, language, UiLanguage.SupportedMarkerList);
                                return Default();
                            }

                            break;

                        default:
                            error = string.Format(Resources.CliUnknownOption, arg);
                            return Default();
                    }

                    continue;
                }

                if (positionalSeen || configPath is not null)
                {
                    error = string.Format(Resources.CliMultipleConfigFiles, arg);
                    return Default();
                }

                configPath = arg;
                positionalSeen = true;
            }

            return new CliOptions(configPath, once, showHelp, showVersion, noColor, noPause, verbose, language);

            CliOptions Default() => new(null, false, false, false, false, false, false, null);
        }
    }
}
