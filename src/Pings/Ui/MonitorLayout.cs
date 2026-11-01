namespace Pings.Ui
{
    /// <summary>实时表格的列。</summary>
    public enum MonitorColumn
    {
        Name,
        Target,
        Status,
        Delay,
        Loss,
        Log
    }

    /// <summary>
    /// 实时表格的显示决策：宽度决定显示哪些列，高度决定显示多少行。
    /// </summary>
    /// <remarks>
    /// 这里只决定「渲染哪些内容」，文本的宽度测量与折行全部交给 Spectre.Console，
    /// 不自行计算字符显示宽度。尺寸取自 Spectre 探测到的
    /// <c>AnsiConsole.Profile.Width</c> / <c>Profile.Height</c>。
    ///
    /// 决策本身是纯函数，便于单元测试——终端尺寸相关的显示问题很难靠肉眼复现。
    /// 目标是：窗口再窄也不折行（宁可少显示几列），窗口再矮也不滚动（宁可少显示几行）。
    /// </remarks>
    public static class MonitorLayout
    {
        /// <summary>
        /// 表格除数据行以外的固定开销：标题 1 + 上边框 1 + 表头 1 + 表头分隔 1 +
        /// 下边框 1 + 说明 1 + 预留 1（避免最后一行贴住提示符被顶出屏幕）。
        /// </summary>
        private const int TableChromeRows = 7;

        /// <summary>至少保留一个数据行，否则表格毫无意义。</summary>
        public const int MinDataRows = 1;

        /// <summary>可以完整显示所有列的最小宽度。</summary>
        public const int FullWidthColumns = 92;

        /// <summary>给定可用高度，最多能放多少个数据行（不触发滚动）。</summary>
        public static int DataRowCapacity(int terminalHeight)
        {
            int capacity = terminalHeight - TableChromeRows;
            return capacity < MinDataRows ? MinDataRows : capacity;
        }

        /// <summary>
        /// 按可用宽度选择列。宽度不足时依次丢弃「日志 → 延迟 → 丢包率 → 目标」，
        /// 始终保留「名称 + 状态」——状态是监控的全部意义。
        /// </summary>
        /// <remarks>
        /// 阈值留得偏宽，是为了让每列都有足够宽度显示有意义的内容（否则名称列会吃掉
        /// 大部分空间，其他列被压到只剩一两个字符）；窗口窄到放不下时宁可少显示一列。
        /// </remarks>
        public static IReadOnlyList<MonitorColumn> ColumnsFor(int terminalWidth) => terminalWidth switch
        {
            >= FullWidthColumns => [MonitorColumn.Name, MonitorColumn.Target, MonitorColumn.Status, MonitorColumn.Delay, MonitorColumn.Loss, MonitorColumn.Log],
            >= 76 => [MonitorColumn.Name, MonitorColumn.Target, MonitorColumn.Status, MonitorColumn.Delay, MonitorColumn.Loss],
            >= 62 => [MonitorColumn.Name, MonitorColumn.Status, MonitorColumn.Delay, MonitorColumn.Loss, MonitorColumn.Log],
            >= 50 => [MonitorColumn.Name, MonitorColumn.Status, MonitorColumn.Delay, MonitorColumn.Loss],
            >= 36 => [MonitorColumn.Name, MonitorColumn.Status, MonitorColumn.Delay],
            _ => [MonitorColumn.Name, MonitorColumn.Status]
        };

        /// <summary>
        /// 超出容量时的取舍结果：<paramref name="Visible"/> 为将要显示的元素，
        /// <paramref name="Hidden"/> 为被省略的数量。
        /// </summary>
        public readonly record struct Selection<T>(IReadOnlyList<T> Visible, int Hidden);

        /// <summary>
        /// 从 <paramref name="items"/> 里挑出最多 <paramref name="capacity"/> 个元素。
        /// 需要截断时优先保留 <paramref name="isFault"/> 为真的元素（异常目标更重要），
        /// 其余按原始顺序填充，避免「出问题的目标恰好被窗口高度挡掉」。
        /// </summary>
        public static Selection<T> Select<T>(IReadOnlyList<T> items, int capacity, Func<T, bool> isFault)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(isFault);

            if (capacity <= 0)
                return new Selection<T>([], items.Count);

            if (items.Count <= capacity)
                return new Selection<T>(items, 0);

            List<T> visible = new(capacity);

            foreach (T item in items)
            {
                if (visible.Count >= capacity)
                    break;

                if (isFault(item))
                    visible.Add(item);
            }

            foreach (T item in items)
            {
                if (visible.Count >= capacity)
                    break;

                if (!isFault(item))
                    visible.Add(item);
            }

            return new Selection<T>(visible, items.Count - visible.Count);
        }
    }
}
