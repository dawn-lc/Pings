using Pings.Monitoring;

namespace Pings.Tests;

public class ProbeStatisticsTests
{
    [Fact]
    public void 统计窗口内的丢包率与延迟极值()
    {
        ProbeStatistics statistics = new(windowSize: 4);

        statistics.Record(ProbeStatus.Success, 10);
        statistics.Record(ProbeStatus.Success, 20);
        statistics.Record(ProbeStatus.Timeout);
        statistics.Record(ProbeStatus.Success, 30);

        ProbeStatisticsSnapshot snapshot = statistics.Snapshot();

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
        ProbeStatistics statistics = new(windowSize: 2);

        statistics.Record(ProbeStatus.Success, 10);
        statistics.Record(ProbeStatus.Timeout);
        statistics.Record(ProbeStatus.Success, 20);
        statistics.Record(ProbeStatus.Success, 30);

        ProbeStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(4, snapshot.Sent);
        Assert.Equal(3, snapshot.Received);
        Assert.Equal(0, snapshot.WindowLossRate);
        Assert.Equal(0.25, snapshot.TotalLossRate, 3);
        Assert.Equal(20, snapshot.MinRttMs);
        Assert.Equal(30, snapshot.MaxRttMs);
    }

    [Fact]
    public void 端口关闭不计入丢包率但计入故障率()
    {
        ProbeStatistics statistics = new(windowSize: 8);

        statistics.Record(ProbeStatus.Success, 10);
        statistics.Record(ProbeStatus.PortClosed);
        statistics.Record(ProbeStatus.DnsFailure);
        statistics.Record(ProbeStatus.Timeout);

        ProbeStatisticsSnapshot snapshot = statistics.Snapshot();

        // 只有一次「完全没有回应」的探测。
        Assert.Equal(0.25, snapshot.WindowLossRate, 3);

        // 4 个样本里 3 个故障（端口关闭 / DNS 失败 / 超时）。
        Assert.Equal(0.75, snapshot.WindowFailureRate, 3);
        Assert.Equal(3, snapshot.Failed);
    }

    [Fact]
    public void 窗口为空时各项为零()
    {
        ProbeStatistics statistics = new(windowSize: 8);

        ProbeStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(0, snapshot.Sent);
        Assert.Equal(0, snapshot.WindowLossRate);
        Assert.Equal(0, snapshot.WindowFailureRate);
        Assert.Null(snapshot.MinRttMs);
        Assert.Null(snapshot.AverageRttMs);
        Assert.Null(snapshot.MaxRttMs);
        Assert.Null(snapshot.P95RttMs);
        Assert.Null(snapshot.StdDevMs);
        Assert.Null(snapshot.JitterMs);
    }

    [Fact]
    public void 抖动随延迟波动增大()
    {
        ProbeStatistics stable = new(windowSize: 16);
        foreach (double rtt in new[] { 10.0, 10.0, 10.0, 10.0 })
            stable.Record(ProbeStatus.Success, rtt);

        ProbeStatistics jittery = new(windowSize: 16);
        foreach (double rtt in new[] { 10.0, 90.0, 10.0, 90.0 })
            jittery.Record(ProbeStatus.Success, rtt);

        Assert.True(jittery.Snapshot().JitterMs > stable.Snapshot().JitterMs);
    }

    [Fact]
    public void 跨丢包抖动重新起算()
    {
        ProbeStatistics statistics = new(windowSize: 8);
        statistics.Record(ProbeStatus.Success, 10);
        statistics.Record(ProbeStatus.Success, 90);

        Assert.NotNull(statistics.Snapshot().JitterMs);

        statistics.Record(ProbeStatus.Timeout);

        Assert.Null(statistics.Snapshot().JitterMs);
    }

    [Fact]
    public void 最近RTT跨丢包保留()
    {
        ProbeStatistics statistics = new(windowSize: 8);
        statistics.Record(ProbeStatus.Success, 42);
        statistics.Record(ProbeStatus.Timeout);

        Assert.Equal(42, statistics.Snapshot().LastRttMs);
    }

    [Fact]
    public void 标准差与P95只统计成功样本()
    {
        ProbeStatistics statistics = new(windowSize: 32);

        for (int i = 1; i <= 20; i++)
            statistics.Record(ProbeStatus.Success, i);

        ProbeStatisticsSnapshot snapshot = statistics.Snapshot();

        // p95 = ceil(0.95 * 20) - 1 = 第 19 个样本（值 19）
        Assert.Equal(19, snapshot.P95RttMs);

        double expectedStdDev = Math.Sqrt(33.25);
        Assert.Equal(expectedStdDev, snapshot.StdDevMs!.Value, 3);
    }

    [Fact]
    public void Reset清空所有统计()
    {
        ProbeStatistics statistics = new(windowSize: 4);
        statistics.Record(ProbeStatus.Success, 10);
        statistics.Record(ProbeStatus.Timeout);

        statistics.Reset();
        ProbeStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(0, snapshot.Sent);
        Assert.Equal(0, snapshot.Received);
        Assert.Equal(0, snapshot.Lost);
        Assert.Equal(0, snapshot.Failed);
        Assert.Equal(0, snapshot.WindowCount);
        Assert.Equal(0, snapshot.WindowLossRate);
        Assert.Null(snapshot.LastRttMs);
    }

    [Fact]
    public void 窗口大小必须为正()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeStatistics(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeStatistics(-1));
    }

    [Fact]
    public void 零延迟仍算收到()
    {
        // 回环地址 RTT 就是 0ms，不能把 0 当成丢包。
        ProbeStatistics statistics = new(windowSize: 4);
        statistics.Record(ProbeStatus.Success, 0);

        ProbeStatisticsSnapshot snapshot = statistics.Snapshot();

        Assert.Equal(1, snapshot.Received);
        Assert.Equal(0, snapshot.Lost);
        Assert.Equal(0, snapshot.WindowLossRate);
        Assert.Equal(0, snapshot.MinRttMs);
    }
}
