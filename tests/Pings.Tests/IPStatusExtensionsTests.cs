using System.Net.NetworkInformation;
using Pings.Monitoring;

namespace Pings.Tests;

public class IPStatusExtensionsTests
{
    [Theory]
    [InlineData(IPStatus.Success, IcmpFaultCategory.None)]
    [InlineData(IPStatus.TimedOut, IcmpFaultCategory.ConnectivityLoss)]
    [InlineData(IPStatus.DestinationHostUnreachable, IcmpFaultCategory.ConnectivityLoss)]
    [InlineData(IPStatus.DestinationProhibited, IcmpFaultCategory.AccessDenied)]
    [InlineData(IPStatus.BadRoute, IcmpFaultCategory.RoutingError)]
    [InlineData(IPStatus.TtlExpired, IcmpFaultCategory.PacketLifetimeExceeded)]
    [InlineData(IPStatus.BadHeader, IcmpFaultCategory.PacketFormatError)]
    [InlineData(IPStatus.NoResources, IcmpFaultCategory.ResourceExhausted)]
    [InlineData(IPStatus.HardwareError, IcmpFaultCategory.HardwareFailure)]
    [InlineData(IPStatus.Unknown, IcmpFaultCategory.Unknown)]
    public void 分类符合预期(IPStatus status, IcmpFaultCategory expected)
    {
        Assert.Equal(expected, status.Classify());
    }

    [Fact]
    public void 所有状态都有中文描述()
    {
        foreach (IPStatus status in Enum.GetValues<IPStatus>())
        {
            string text = status.ToChineseString();
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void 成功状态描述为通讯正常()
    {
        Assert.Equal("通讯正常", IPStatus.Success.ToChineseString());
        Assert.Equal("请求超时", IPStatus.TimedOut.ToChineseString());
    }
}
