using Pings.Monitoring;
using Pings.Properties;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pings.Ui
{
    /// <summary>
    /// 实时监控视图，全部基于 Spectre.Console 渲染。
    /// </summary>
    /// <remarks>
    /// 每个刷新周期读一次任务快照并重建表格；任务事件不碰 UI，
    /// 因此不存在「后台线程改表格、前台渲染读表格」的竞态。
    ///
    /// 显示问题全部用 Spectre 自身能力解决：
    /// <list type="bullet">
    /// <item><c>AnsiConsole.AlternateScreen</c>：在终端备用缓冲区里绘制，
    ///   退出时（含异常）由 Spectre 在 finally 中整屏还原，
    ///   滚动历史与命令提示符不受干扰；</item>
    /// <item><c>Overflow.Ellipsis</c>：超长文本截断并显示省略号，不让表格被撑高；</item>
    /// <item><c>AnsiConsole.Profile.Width/Height</c>：用 Spectre 探测的尺寸
    ///   决定显示哪些列、多少行，窄窗口不会溢出到下一行。</item>
    /// </list>
    /// </remarks>
    public sealed class MonitorView
    {
        private readonly ProbeMonitor _monitor;
        private readonly TimeSpan _refreshInterval;
        private readonly Action? _prepareAlternateScreen;

        /// <param name="prepareAlternateScreen">
        /// 进入备用缓冲区之后、首帧绘制之前执行的终端准备（例如修正字体）。
        /// 必须放在备用缓冲区<strong>内部</strong>执行：切换缓冲区会重置窗口字体，
        /// 在外面设好的字体进入后会被覆盖回控制台默认字体。
        /// </param>
        public MonitorView(ProbeMonitor monitor, TimeSpan refreshInterval, Action? prepareAlternateScreen = null)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _refreshInterval = refreshInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : refreshInterval;
            _prepareAlternateScreen = prepareAlternateScreen;
        }

        /// <summary>
        /// 在备用缓冲区中持续刷新表格，直到 <paramref name="token"/> 取消。
        /// </summary>
        /// <remarks>
        /// Spectre 的 <c>AlternateScreen</c> 在终端不支持 ANSI 时会直接抛
        /// <c>NotSupportedException</c>（而不是静默降级），所以这里先用它自己的
        /// 能力探测（<c>Profile.Capabilities.Ansi</c>）决定是否启用，
        /// 不支持时退化为普通渲染。
        /// </remarks>
        public Task RunLiveAsync(CancellationToken token)
        {
            // 用 Spectre 自己探测的 AlternateBuffer 能力做守卫：
            // AlternateScreen 在终端不支持时是抛 NotSupportedException（不是静默降级）。
            bool useAlternateScreen = AnsiConsole.Profile.Capabilities.AlternateBuffer;

            // AlternateScreen 是同步 API（Action 作用域，内部 finally 还原终端），
            // 包成任务避免阻塞调用方；取消令牌不传给 Task.Run，
            // 保证「进入/退出缓冲区」一定配对执行。
            return Task.Run(
                () =>
                {
                    if (useAlternateScreen)
                    {
                        AnsiConsole.AlternateScreen(() =>
                        {
                            _prepareAlternateScreen?.Invoke();
                            RunLiveLoopAsync(token).GetAwaiter().GetResult();
                        });
                    }
                    else
                    {
                        RunLiveLoopAsync(token).GetAwaiter().GetResult();
                    }
                },
                CancellationToken.None);
        }

        private async Task RunLiveLoopAsync(CancellationToken token)
        {
            try
            {
                await AnsiConsole.Live(BuildTable())
                    // 溢出策略显式写出来，因为行高稳定性完全依赖它：垂直方向用 Ellipsis 截断而不是 Fold，
                    // 否则内容变高会让 Live 的上移量与实际不符，画面逐帧错位。
                    // 裁剪方向选 Bottom：默认的 Top 在行数算多或窗口被拉窄时裁掉的正是标题与表头。
                    .Overflow(VerticalOverflow.Ellipsis)
                    .Cropping(VerticalOverflowCropping.Bottom)
                    .StartAsync(async context =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            // 不要再跟一个 context.Refresh()：UpdateTarget 内部已经调用 Refresh
                            // （实测 0.57.2 的 IL），多调一次等于每帧把整张表渲染两遍。
                            context.UpdateTarget(BuildTable());

                            try
                            {
                                await Task.Delay(_refreshInterval, token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                    })
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>输出一次性表格（--once 模式），同样按当前终端尺寸适配。</summary>
        public void RenderOnce() => AnsiConsole.Write(BuildTable());

        private Table BuildTable()
        {
            IReadOnlyList<ProbeTaskSnapshot> snapshots = _monitor.Snapshots();
            IReadOnlyList<MonitorColumn> columns = MonitorLayout.ColumnsFor(AnsiConsole.Profile.Width);
            int capacity = MonitorLayout.DataRowCapacity(AnsiConsole.Profile.Height);

            MonitorLayout.Selection<ProbeTaskSnapshot> selection = MonitorLayout.Select(
                snapshots,
                capacity,
                snapshot => snapshot.Category != ProbeFaultCategory.None);

            long droppedWarnings = 0;
            foreach (ProbeTaskSnapshot snapshot in snapshots)
                droppedWarnings += snapshot.DroppedWarnings;

            Table table = new()
            {
                Title = new TableTitle(string.Format(Resources.MonitorTitle, DateTime.Now.ToString(Resources.DateTimeFormat))),
                Caption = new TableTitle(BuildCaption(selection.Hidden, columns.Count < MonitorLayout.AllColumns.Count, droppedWarnings)),
                Expand = true
            };

            foreach (MonitorColumn column in columns)
                table.AddColumn(new TableColumn(HeaderOf(column)));

            if (selection.Visible.Count == 0)
            {
                table.AddRow([.. columns.Select(Placeholder)]);
                return table;
            }

            foreach (ProbeTaskSnapshot snapshot in selection.Visible)
                table.AddRow([.. columns.Select(column => (IRenderable)BuildCell(column, snapshot))]);

            return table;
        }

        private static string HeaderOf(MonitorColumn column) => column switch
        {
            MonitorColumn.Name => Resources.MonitorColumnName,
            MonitorColumn.Target => Resources.MonitorColumnTarget,
            MonitorColumn.Status => Resources.MonitorColumnStatus,
            MonitorColumn.Delay => Resources.MonitorColumnDelay,
            MonitorColumn.Rtt => Resources.MonitorColumnRtt,
            MonitorColumn.Loss => Resources.MonitorColumnLoss,
            MonitorColumn.Log => Resources.MonitorColumnLog,
            _ => string.Empty
        };

        private static IRenderable Placeholder(MonitorColumn column) => column switch
        {
            MonitorColumn.Status => Cell(Resources.MonitorNoTasks),
            _ => Cell("-")
        };

        private static string BuildCaption(int hidden, bool columnsTrimmed, long droppedWarnings)
        {
            List<string> parts = [Resources.MonitorCaptionAcknowledge];

            if (hidden > 0)
                parts.Add(string.Format(Resources.MonitorHiddenTargets, hidden));

            if (columnsTrimmed)
                parts.Add(Resources.MonitorColumnsTrimmed);

            if (droppedWarnings > 0)
                parts.Add(string.Format(Resources.MonitorDroppedWarnings, droppedWarnings));

            return string.Join(" · ", parts);
        }

        /// <summary>
        /// 构建单元格，超长文本用 <see cref="Overflow.Ellipsis"/> 截断并显示省略号。
        /// </summary>
        /// <remarks>
        /// 行高要稳定：单元格一旦折行，表格高度会随内容膨胀（实测 5 个任务在窄窗口下
        /// 可涨到 30+ 行），越出窗口后表格被顶成滚动，实时刷新就会错位。
        ///
        /// 关于 Spectre 的版本敏感点（都在 0.57.2 上实测过）：
        /// <list type="bullet">
        /// <item>0.54 下 <c>NoWrap</c> 会死循环、<c>Crop</c>/<c>Ellipsis</c> 遇中文会抛
        ///   <c>ArgumentOutOfRangeException</c>，所以当时只能退回默认的 <c>Fold</c>；</item>
        /// <item>0.57.2 已修复上述两个问题，但 <c>Fold</c> 反而会在窄窗口挂死，
        ///   因此绝不能再显式指定/退回 <c>Fold</c>。</item>
        /// </list>
        ///
        /// 注意 <c>TableColumn.NoWrap</c> 只影响列宽测算，并不会阻止空格式换行
        /// （如 “171ms ±2.3ms”），所以不要把“不折行”寄托在它身上。
        /// </remarks>
        private static Text BuildCell(MonitorColumn column, ProbeTaskSnapshot snapshot) => column switch
        {
            MonitorColumn.Name => Cell(snapshot.Name),
            MonitorColumn.Target => Cell(snapshot.Endpoint),
            MonitorColumn.Status => Cell(snapshot.StatusText, StatusStyle(snapshot.Category)),
            MonitorColumn.Delay => Cell(FormatDelay(snapshot)),
            MonitorColumn.Rtt => Cell(snapshot.RttDistributionText),
            MonitorColumn.Loss => Cell(snapshot.LossText, LossStyle(snapshot.Statistics.WindowLossRate)),
            MonitorColumn.Log => Cell(snapshot.Warning is { Length: > 0 } warning
                ? snapshot.WarningCount > 1 ? $"[{snapshot.WarningCount}] {warning}" : warning
                : snapshot.LastEvent,
                snapshot.Warning is null ? Style.Plain : WarningStyle),
            _ => Cell("-")
        };

        private static Text Cell(string text, Style? style = null) => new(text, style ?? Style.Plain)
        {
            Overflow = Overflow.Ellipsis
        };

        /// <summary>
        /// 延迟列文案：有抖动时才附上，避免出现 “0ms -” 这种占位（多一个空格就多一次折行机会）。
        /// </summary>
        private static string FormatDelay(ProbeTaskSnapshot snapshot)
        {
            if (!snapshot.IsSuccess)
                return "-";

            return snapshot.Statistics.JitterMs is not null
                ? $"{snapshot.DelayText} {snapshot.JitterText}"
                : snapshot.DelayText;
        }

        private static readonly Style WarningStyle = new(Color.Yellow, Color.Red, Decoration.Bold);

        // Style 是类：按帧构造会产生每秒数十次无用分配，这些样式在进程内不变，缓存一次即可。
        private static readonly Style StatusOkStyle = new(Color.Green);
        private static readonly Style StatusSilentStyle = new(Color.Red, decoration: Decoration.Bold);
        private static readonly Style StatusServiceStyle = new(Color.Orange1);
        private static readonly Style StatusNameStyle = new(Color.Yellow);
        private static readonly Style StatusProtocolStyle = new(Color.Orange1);
        private static readonly Style StatusLocalStyle = new(Color.Grey);
        private static readonly Style StatusUnknownStyle = new(Color.Grey);

        private static Style StatusStyle(ProbeFaultCategory category) => category switch
        {
            ProbeFaultCategory.None => StatusOkStyle,
            ProbeFaultCategory.NoResponse => StatusSilentStyle,
            ProbeFaultCategory.ServiceUnavailable => StatusServiceStyle,
            ProbeFaultCategory.NameResolution => StatusNameStyle,
            ProbeFaultCategory.ProtocolError => StatusProtocolStyle,
            ProbeFaultCategory.LocalResource => StatusLocalStyle,
            _ => StatusUnknownStyle
        };

        private static readonly Style LossOkStyle = new(Color.Green);
        private static readonly Style LossLowStyle = new(Color.Yellow);
        private static readonly Style LossHighStyle = new(Color.Red);

        private static Style LossStyle(double rate) => rate switch
        {
            <= 0 => LossOkStyle,
            < 0.05 => LossLowStyle,
            _ => LossHighStyle
        };
    }
}
