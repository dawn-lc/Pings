namespace Pings.Cli
{
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
            Success => "正常结束",
            InvalidArguments => "命令行参数错误",
            Configuration => "配置文件错误",
            RuntimeError => "运行期错误",
            Unreachable => "存在不可达目标",
            _ => "未知退出码"
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
        bool Verbose)
    {
        public const string Usage = """
            用法: Pings [配置文件] [选项]

            参数:
              配置文件                 JSON 配置路径，默认按当前目录 config.json、
                                      再按程序目录 config.json 查找

            选项:
              -c, --config <路径>      指定配置文件
                  --once               只探测一轮并输出表格后退出（适合脚本/CI）
                  --no-color           关闭彩色输出
                  --no-pause           出错时不再等待按键
                  --verbose            出错时输出完整堆栈
              -h, --help               显示帮助
              -v, --version            显示版本

            运行时快捷键:
              C  确认当前警告
              Q  退出

            退出码:
              0 正常结束      1 参数错误      2 配置错误
              3 运行期错误    4 存在不可达目标（--once）
            """;

        public static CliOptions Parse(string[] args, out string? error)
        {
            error = null;

            string? configPath = null;
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
                                error = $"参数 {arg} 缺少路径";
                                return Default();
                            }

                            configPath = args[++i];
                            break;

                        default:
                            error = $"未知参数: {arg}";
                            return Default();
                    }

                    continue;
                }

                if (positionalSeen || configPath is not null)
                {
                    error = $"只能指定一个配置文件（多余的参数: {arg}）";
                    return Default();
                }

                configPath = arg;
                positionalSeen = true;
            }

            return new CliOptions(configPath, once, showHelp, showVersion, noColor, noPause, verbose);

            CliOptions Default() => new(null, false, false, false, false, false, false);
        }
    }
}
