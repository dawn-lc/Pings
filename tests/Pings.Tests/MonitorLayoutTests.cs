using Pings.Ui;

namespace Pings.Tests;

public class MonitorLayoutTests
{
    [Fact]
    public void 宽窗口显示全部六列()
    {
        IReadOnlyList<MonitorColumn> columns = MonitorLayout.ColumnsFor(120);

        Assert.Equal(6, columns.Count);
        Assert.Contains(MonitorColumn.Log, columns);
        Assert.Contains(MonitorColumn.Target, columns);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(92)]
    [InlineData(76)]
    [InlineData(62)]
    [InlineData(50)]
    [InlineData(36)]
    [InlineData(10)]
    public void 任何宽度都保留名称与状态(int width)
    {
        IReadOnlyList<MonitorColumn> columns = MonitorLayout.ColumnsFor(width);

        Assert.Contains(MonitorColumn.Name, columns);
        Assert.Contains(MonitorColumn.Status, columns);
        Assert.NotEmpty(columns);
    }

    [Fact]
    public void 窗口变窄时逐级丢弃次要列()
    {
        Assert.True(MonitorLayout.ColumnsFor(120).Count >= MonitorLayout.ColumnsFor(80).Count);
        Assert.True(MonitorLayout.ColumnsFor(80).Count >= MonitorLayout.ColumnsFor(65).Count);
        Assert.True(MonitorLayout.ColumnsFor(65).Count >= MonitorLayout.ColumnsFor(55).Count);
        Assert.True(MonitorLayout.ColumnsFor(55).Count >= MonitorLayout.ColumnsFor(40).Count);
        Assert.True(MonitorLayout.ColumnsFor(40).Count >= MonitorLayout.ColumnsFor(20).Count);
    }

    [Fact]
    public void 极窄窗口只留两列()
    {
        Assert.Equal(2, MonitorLayout.ColumnsFor(20).Count);
    }

    [Fact]
    public void 列不重复()
    {
        foreach (int width in new[] { 200, 100, 92, 76, 62, 50, 36, 5 })
        {
            IReadOnlyList<MonitorColumn> columns = MonitorLayout.ColumnsFor(width);
            Assert.Equal(columns.Count, columns.Distinct().Count());
        }
    }

    [Fact]
    public void 宽度不足时不会给出过多列()
    {
        // 列数过多时 Spectre 在窄终端可能布置失败（宁死循环/抛异常），
        // 这里守护“列数随宽度单调、且窄宽度下很少”。
        foreach (int width in new[] { 20, 30, 40, 48 })
            Assert.True(MonitorLayout.ColumnsFor(width).Count <= 3, $"宽度 {width} 时列过多");
    }

    [Theory]
    [InlineData(40, 33)]
    [InlineData(30, 23)]
    [InlineData(8, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void 行容量等于高度减表格开销且至少一行(int height, int expected)
    {
        Assert.Equal(expected, MonitorLayout.DataRowCapacity(height));
    }

    [Fact]
    public void 控件未超出容量时全部显示()
    {
        MonitorLayout.Selection<int> selection = MonitorLayout.Select([1, 2, 3], 5, _ => false);

        Assert.Equal([1, 2, 3], selection.Visible);
        Assert.Equal(0, selection.Hidden);
    }

    [Fact]
    public void 超出容量时优先保留异常项()
    {
        // 中间那个是异常目标，容量只够 2 个：必须保留它。
        int[] items = [1, 2, 3, 4];
        bool[] faults = [false, false, true, false];

        MonitorLayout.Selection<int> selection = MonitorLayout.Select(
            items,
            2,
            item => faults[item - 1]);

        Assert.Contains(3, selection.Visible);
        Assert.Equal(2, selection.Visible.Count);
        Assert.Equal(2, selection.Hidden);
    }

    [Fact]
    public void 容量为零时全部隐藏()
    {
        MonitorLayout.Selection<int> selection = MonitorLayout.Select([1, 2], 0, _ => false);

        Assert.Empty(selection.Visible);
        Assert.Equal(2, selection.Hidden);
    }

    [Fact]
    public void 空列表不报错()
    {
        MonitorLayout.Selection<int> selection = MonitorLayout.Select<int>([], 3, _ => true);

        Assert.Empty(selection.Visible);
        Assert.Equal(0, selection.Hidden);
    }
}
