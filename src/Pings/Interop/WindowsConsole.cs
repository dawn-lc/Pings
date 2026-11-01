#if WINDOWS
using System.Runtime.InteropServices;

namespace Pings.Interop
{
    /// <summary>
    /// Windows 控制台相关的最小互操作集合（非 Windows 目标不参与编译）。
    /// </summary>
    internal static partial class WindowsConsole
    {
        private const int STD_OUTPUT_HANDLE = -11;
        private const uint CP_UTF8 = 65001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CONSOLE_FONT_INFO_EX
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

        public delegate bool HandlerRoutine(int ctrlType);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GetStdHandle(int nStdHandle);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetConsoleOutputCP(uint wCodePageID);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetConsoleCP(uint wCodePageID);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCurrentConsoleFontEx(
            IntPtr consoleOutput,
            [MarshalAs(UnmanagedType.Bool)] bool maximumWindow,
            ref CONSOLE_FONT_INFO_EX consoleFontEx);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetConsoleCtrlHandler(HandlerRoutine handler, [MarshalAs(UnmanagedType.Bool)] bool add);

        /// <summary>把输入/输出代码页切换为 UTF-8（否则中文在旧代码页下会乱码）。</summary>
        public static void EnableUtf8Support()
        {
            if (!SetConsoleOutputCP(CP_UTF8))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法设置控制台输出代码页为 UTF-8");

            if (!SetConsoleCP(CP_UTF8))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法设置控制台输入代码页为 UTF-8");
        }

        /// <summary>强制设置控制台字体，保证方块字符与表格边框可显示。</summary>
        public static bool ForceSetConsoleFont(string fontName, short fontSizeY = 16)
        {
            IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return false;

            CONSOLE_FONT_INFO_EX info = new()
            {
                cbSize = (uint)Marshal.SizeOf<CONSOLE_FONT_INFO_EX>(),
                FaceName = fontName,
                dwFontSizeX = 0,
                dwFontSizeY = fontSizeY,
                FontFamily = 54,
                FontWeight = 400
            };

            return SetCurrentConsoleFontEx(handle, false, ref info);
        }

        /// <summary>
        /// 备选字体，按优先级排列。全部取自微软官方的 Windows 字体清单（<c>typography/fonts/windows_10_font_list</c>、
        /// <c>windows_11_font_list</c>）中<strong>主表</strong>而非「按需语言包（FOD）」的等宽字体，
        /// 因此不随系统语言变化。
        /// </summary>
        /// <remarks>
        /// 实测（简体中文 Windows 11，控制台 120x30）：
        /// <code>
        /// 字体              ± → … · °   中文   制表符
        /// Consolas          1 1 1 1 1    2     1
        /// Lucida Console    1 1 1 1 1    2     1
        /// Courier New       1 1 1 1 1    2     1
        /// MS Gothic         2 2 2 1 2    2     1   ← 歧义宽算两格，不可用
        /// 新宋体 (NSimSun)   2 2 2 2 2    2     1   ← 中文 Windows 默认字体，正是问题来源
        /// </code>
        /// Cascadia Mono / Code 同样合格，但只在 Windows 11 随系统提供（Windows 10 需另装），
        /// 所以不作为首选。
        ///
        /// 拉丁字体没有 CJK 字形，中文由 conhost 自行回退到系统 CJK 字体渲染（<c>SystemLink</c> 里
        /// Consolas 并无条目，但实测能正常显示），因此换字体不会让中文变方块。
        /// </remarks>
        private static readonly string[] PreferredFonts = ["Consolas", "Lucida Console", "Courier New"];

        /// <summary>
        /// 确认终端把「歧义宽度」字符（<c>± → … · °</c>）当成一格，必要时换成拉丁等宽字体。
        /// </summary>
        /// <remarks>
        /// Spectre.Console 按 Unicode East Asian Width 计算列宽，把 Ambiguous 类字符算作一格；
        /// 而东亚语言的默认控制台字体（简体中文 Windows 上是新宋体 8x16）把它们画成两格。
        /// 这个差异会让 Spectre 认为「正好铺满一行」的内容在屏幕上多出一两格并折行，
        /// 备用缓冲区里 <c>Live</c> 的光标回退量随即与实际不符，画面逐帧错位
        /// （实测：延迟列的 <c>±</c>、日志里的 <c>→</c>、说明行的 <c>·</c> 都会触发）。
        ///
        /// 字形宽度是宿主属性，程序能做的只有把字体换成 Ambiguous 为一格的等宽字体；
        /// 因此这里用「写一个 <c>±</c> 看光标前进几格」实测，只在确实为两格时才切换，
        /// 不覆盖用户已经调好的字体。注意必须实测：中文在两种字体下都是两格，不能靠它判断。
        ///
        /// 候选只取「各国语言版本的 Windows 都预装」的等宽字体（见 <see cref="PreferredFonts" />）。
        /// </remarks>
        /// <returns>切换成功或本来就不需要切换时为 <see langword="true" />。</returns>
        public static bool EnsureNarrowAmbiguousWidth(short fontSizeY = 18)
        {
            // 输出被重定向时没有格子概念，也改不了字体。
            if (Console.IsOutputRedirected)
                return true;

            if (MeasureAmbiguousWidth() <= 1)
                return true;

            foreach (string font in PreferredFonts)
            {
                if (ForceSetConsoleFont(font, fontSizeY) && MeasureAmbiguousWidth() <= 1)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 在光标处写一个歧义宽度字符，返回它占用的字符格数；写完把光标与内容还原。
        /// </summary>
        private static int MeasureAmbiguousWidth()
        {
            try
            {
                int left = Console.CursorLeft;
                int top = Console.CursorTop;

                Console.Write('±');
                int advance = Console.CursorLeft - left;

                // 抹掉探针字符并回到原位：Live 首帧从当前光标位置开始绘制，
                // 残留内容会顶到表格左边框外面。
                Console.SetCursorPosition(left, top);
                Console.Write(new string(' ', Math.Max(advance, 1)));
                Console.SetCursorPosition(left, top);

                return advance <= 0 ? 1 : advance;
            }
            catch (IOException)
            {
                return 1;
            }
            catch (ArgumentOutOfRangeException)
            {
                return 1;
            }
        }

        /// <summary>
        /// 注册控制台关闭/注销事件。返回 true 表示由我们的处理函数接管，
        /// 让主流程有机会正常收尾（刷盘日志、停止任务）。
        /// </summary>
        public static void RegisterShutdown(Action onShutdown)
        {
            SetConsoleCtrlHandler(_ =>
            {
                onShutdown();
                return true;
            }, add: true);
        }
    }
}
#endif
