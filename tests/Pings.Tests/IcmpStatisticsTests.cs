using Pings.Monitoring;

namespace Pings.Tests;

public class IcmpStatisticsTests
{
    [Fact]
    public void 统计窗口内的丢包率与延迟极值()
    {
        IcmpStatistics statistics = new(windowSize: 4);

        statistics.Record(10);
        statistics.Record(20);
        statistics.Record(null);
        statistics.Record(30);

        IcmpStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(4, snapshot.Sent);
        Assert.Equal(3, snapshot.Received);
        Assert.Equal(1, snapshot.Lost);
        Assert.Equal(0.25, snapshot.WindowLossRate, 3);
        Assert.Equal(0.25, snapshot.TotalLossRate, 3);
        Assert.Equal(10, snapshot.MinRttMs);
        Assert.Equal(30, snapshot.MaxRttMs);
        Assert.Equal(20, snapshot.AverageRttMs);
    }

    [Fact]
    public void 滑窗满后旧样本被覆盖()
    {
        IcmpStatistics statistics = new(windowSize: 2);

        statistics.Record(10);
        statistics.Record(null);
        statistics.Record(20);
        statistics.Record(30);

        IcmpStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(4, snapshot.Sent);
        Assert.Equal(3, snapshot.Received);
        Assert.Equal(0, snapshot.WindowLossRate);
        Assert.Equal(0.25, snapshot.TotalLossRate, 3);
        Assert.Equal(20, snapshot.MinRttMs);
        Assert.Equal(30, snapshot.MaxRttMs);
    }

    [Fact]
    public void 窗口为空时各项为零()
    {
        IcmpStatistics statistics = new(windowSize: 8);

        IcmpStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(0, snapshot.Sent);
        Assert.Equal(0, snapshot.WindowLossRate);
        Assert.Null(snapshot.MinRttMs);
        Assert.Null(snapshot.AverageRttMs);
        Assert.Null(snapshot.MaxRttMs);
    }

    [Fact]
    public void 抖动随延迟波动增大()
    {
        IcmpStatistics stable = new(windowSize: 16);
        foreach (double rtt in new[] { 10.0, 10.0, 10.0, 10.0 })
            stable.Record(rtt);

        IcmpStatistics jittery = new(windowSize: 16);
        foreach (double rtt in new[] { 10.0, 90.0, 10.0, 90.0 })
            jittery.Record(rtt);

        Assert.True(jittery.Snapshot().JitterMs > stable.Snapshot().JitterMs);
    }

    [Fact]
    public void Reset清空所有统计()
    {
        IcmpStatistics statistics = new(windowSize: 4);
        statistics.Record(10);
        statistics.Record(null);

        statistics.Reset();
        IcmpStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(0, snapshot.Sent);
        Assert.Equal(0, snapshot.Received);
        Assert.Equal(0, snapshot.WindowLossRate);
    }

    [Fact]
    public void 窗口大小必须为正()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IcmpStatistics(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IcmpStatistics(-1));
    }

    [Fact]
    public void 零延迟仍算收到()
    {
        // 回环地址 RTT 就是 0ms，不能把 0 当成丢包。
        IcmpStatistics statistics = new(windowSize: 4);
        statistics.Record(received: true, rttMs: 0);

        IcmpStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(1, snapshot.Received);
        Assert.Equal(0, snapshot.Lost);
        Assert.Equal(0, snapshot.WindowLossRate);
        Assert.Equal(0, snapshot.MinRttMs);
    }
}
