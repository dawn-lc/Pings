using Pings.Configuration;
using Pings.Monitoring;

namespace Pings.Tests;

public class ProbeTaskOptionsTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("example.com")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void 合法主机(string target)
    {
        Assert.True(ProbeTaskOptions.IsValidHost(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad host!")]
    public void 非法主机(string target)
    {
        Assert.False(ProbeTaskOptions.IsValidHost(target));
    }

    [Theory]
    [InlineData("https://example.com/health")]
    [InlineData("http://127.0.0.1:8080/")]
    public void 合法HTTP地址(string target)
    {
        Assert.True(ProbeTaskOptions.IsValidHttpUrl(target));
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    [InlineData("not a url")]
    public void 非法HTTP地址(string target)
    {
        Assert.False(ProbeTaskOptions.IsValidHttpUrl(target));
    }

    [Fact]
    public void 未配置间隔时与超时一致()
    {
        ProbeTaskOptions options = new() { Name = "a", Target = "1.1.1.1", TimeoutMs = 2500 };

        Assert.Equal(2500, options.EffectiveIntervalMs);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), options.PollInterval);
    }

    [Fact]
    public void 显式间隔优先()
    {
        ProbeTaskOptions options = new() { Name = "a", Target = "1.1.1.1", TimeoutMs = 1000, IntervalMs = 300 };

        Assert.Equal(300, options.EffectiveIntervalMs);
    }

    [Fact]
    public void 端点文本包含协议与端口()
    {
        Assert.Equal("icmp://1.1.1.1", new ProbeTaskOptions { Name = "a", Target = "1.1.1.1" }.Endpoint);

        Assert.Equal(
            "tcp://10.0.0.5:5432",
            new ProbeTaskOptions { Name = "b", Target = "10.0.0.5", Protocol = ProbeProtocol.Tcp, Port = 5432 }.Endpoint);

        Assert.Equal(
            "udp://10.0.0.53:5353",
            new ProbeTaskOptions { Name = "c", Target = "10.0.0.53", Protocol = ProbeProtocol.Udp, Port = 5353 }.Endpoint);

        Assert.Equal(
            "https://example.com/health",
            new ProbeTaskOptions { Name = "d", Target = "https://example.com/health", Protocol = ProbeProtocol.Http }.Endpoint);
    }

    [Fact]
    public void 从JSON映射并往返()
    {
        ProbeTaskConfigJson json = new()
        {
            Name = " 网关 ",
            Protocol = "tcp",
            Target = " 192.168.1.1 ",
            Port = 443,
            Timeout = 1500,
            Interval = 200,
            MaxRecentPackets = 32,
            SignificantDelayThreshold = 50,
            PacketLossDuration = 12.5,
            PacketLossCount = 8,
            LossFlapWindowSeconds = 120,
            LossFlapCount = 4
        };

        ProbeTaskOptions options = ProbeTaskOptions.FromJson(json);
        ProbeTaskConfigJson roundTrip = options.ToJson();

        Assert.Equal("网关", options.Name);
        Assert.Equal(ProbeProtocol.Tcp, options.Protocol);
        Assert.Equal("192.168.1.1", options.Target);
        Assert.Equal(443, options.Port);
        Assert.Equal("tcp", roundTrip.Protocol);
        Assert.Equal(1500, roundTrip.Timeout);
        Assert.Equal(200, roundTrip.Interval);
        Assert.Equal(32, roundTrip.MaxRecentPackets);
        Assert.Equal(50, roundTrip.SignificantDelayThreshold);
        Assert.Equal(12.5, roundTrip.PacketLossDuration);
        Assert.Equal(8, roundTrip.PacketLossCount);
        Assert.Equal(120, roundTrip.LossFlapWindowSeconds);
        Assert.Equal(4, roundTrip.LossFlapCount);
    }

    [Fact]
    public void 未写协议时默认ICMP()
    {
        ProbeTaskConfigJson json = new() { Name = "a", Target = "8.8.8.8", Protocol = string.Empty, Timeout = 500 };

        ProbeTaskOptions options = ProbeTaskOptions.FromJson(json);

        Assert.Equal(ProbeProtocol.Icmp, options.Protocol);
    }

    [Theory]
    [InlineData("head", "HEAD")]
    [InlineData("get", "GET")]
    [InlineData("POST", "HEAD")]
    [InlineData("", "HEAD")]
    public void HTTP方法被规范化(string input, string expected)
    {
        Assert.Equal(expected, ProbeTaskOptions.NormalizeHttpMethod(input));
    }

    [Fact]
    public void 快照文本在非成功时显示占位符()
    {
        ProbeStatisticsSnapshot statistics = new(
            Sent: 10,
            Received: 5,
            Lost: 2,
            Failed: 3,
            WindowCount: 10,
            WindowReceived: 5,
            WindowLossRate: 0.5,
            TotalLossRate: 0.2,
            WindowFailureRate: 0.3,
            TotalFailureRate: 0.3,
            MinRttMs: 10,
            AverageRttMs: 12,
            MaxRttMs: 20,
            P95RttMs: 18,
            LastRttMs: 12,
            StdDevMs: 1.5,
            JitterMs: null,
            JitterSamples: 0);

        ProbeTaskSnapshot snapshot = new(
            "a",
            ProbeProtocol.Tcp,
            "1.1.1.1",
            "tcp://1.1.1.1:80",
            ProbeStatus.Timeout,
            null,
            ProbeFaultCategory.NoResponse,
            ProbeSeverity.Warning,
            null,
            null,
            12,
            statistics,
            DateTimeOffset.Now,
            "请求超时",
            null,
            0,
            0);

        Assert.False(snapshot.IsSuccess);
        Assert.Equal("-", snapshot.DelayText);
        Assert.Equal("-", snapshot.JitterText);
        Assert.Equal("50.0%", snapshot.LossText);
        Assert.Equal("30.0%", snapshot.FailureText);
        Assert.Equal("请求超时", snapshot.StatusText);
    }

    [Fact]
    public void 状态文本优先使用协议原始信息()
    {
        ProbeStatisticsSnapshot statistics = default;

        ProbeTaskSnapshot snapshot = new(
            "a",
            ProbeProtocol.Http,
            "https://example.com",
            "https://example.com",
            ProbeStatus.Success,
            "HTTP 204",
            ProbeFaultCategory.None,
            ProbeSeverity.None,
            12,
            3,
            0,
            statistics,
            DateTimeOffset.Now,
            "HTTP 204 12ms",
            null,
            0,
            0);

        Assert.Equal("HTTP 204", snapshot.StatusText);
    }
}
