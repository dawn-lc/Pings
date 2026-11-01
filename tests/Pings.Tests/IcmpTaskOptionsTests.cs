using Pings.Configuration;
using Pings.Monitoring;

namespace Pings.Tests;

public class IcmpTaskOptionsTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("example.com")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void 合法目标(string target)
    {
        Assert.True(IcmpTaskOptions.IsValidTarget(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad host!")]
    public void 非法目标(string target)
    {
        Assert.False(IcmpTaskOptions.IsValidTarget(target));
    }

    [Fact]
    public void 未配置间隔时与超时一致()
    {
        IcmpTaskOptions options = new() { Name = "a", Target = "1.1.1.1", TimeoutMs = 2500 };

        Assert.Equal(2500, options.EffectiveIntervalMs);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), options.PollInterval);
    }

    [Fact]
    public void 显式间隔优先()
    {
        IcmpTaskOptions options = new() { Name = "a", Target = "1.1.1.1", TimeoutMs = 1000, IntervalMs = 300 };

        Assert.Equal(300, options.EffectiveIntervalMs);
    }

    [Fact]
    public void 从JSON映射并往返()
    {
        IcmpTaskConfigJson json = new()
        {
            Name = " 网关 ",
            IP = " 192.168.1.1 ",
            Timeout = 1500,
            Interval = 200,
            MaxRecentPackets = 32,
            SignificantDelayThreshold = 50,
            PacketLossDuration = 12.5,
            PacketLossCount = 8
        };

        IcmpTaskOptions options = IcmpTaskOptions.FromJson(json);
        IcmpTaskConfigJson roundTrip = options.ToJson();

        Assert.Equal("网关", options.Name);
        Assert.Equal("192.168.1.1", options.Target);
        Assert.Equal(1500, roundTrip.Timeout);
        Assert.Equal(200, roundTrip.Interval);
        Assert.Equal(32, roundTrip.MaxRecentPackets);
        Assert.Equal(50, roundTrip.SignificantDelayThreshold);
        Assert.Equal(12.5, roundTrip.PacketLossDuration);
        Assert.Equal(8, roundTrip.PacketLossCount);
    }

    [Fact]
    public void 快照文本在丢包时显示占位符()
    {
        IcmpTaskSnapshot snapshot = new(
            "a", "1.1.1.1", System.Net.NetworkInformation.IPStatus.TimedOut, IcmpFaultCategory.ConnectivityLoss,
            null, 0, 0.5, 0.25, 10, 5, DateTimeOffset.Now, "请求超时", null, 0);

        Assert.False(snapshot.IsSuccess);
        Assert.Equal("-", snapshot.DelayText);
        Assert.Equal("50.0%", snapshot.LossText);
        Assert.Equal("请求超时", snapshot.StatusText);
    }
}
