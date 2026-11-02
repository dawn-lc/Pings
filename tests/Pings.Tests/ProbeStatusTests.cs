using System.Globalization;
using Pings.Monitoring;

namespace Pings.Tests;

public class ProbeStatusTests
{
    [Theory]
    [InlineData(ProbeStatus.Success, ProbeFaultCategory.None)]
    [InlineData(ProbeStatus.Timeout, ProbeFaultCategory.NoResponse)]
    [InlineData(ProbeStatus.NoReply, ProbeFaultCategory.NoResponse)]
    [InlineData(ProbeStatus.ResponseTimeout, ProbeFaultCategory.NoResponse)]
    [InlineData(ProbeStatus.Unreachable, ProbeFaultCategory.NoResponse)]
    [InlineData(ProbeStatus.PortClosed, ProbeFaultCategory.ServiceUnavailable)]
    [InlineData(ProbeStatus.TlsFailure, ProbeFaultCategory.ServiceUnavailable)]
    [InlineData(ProbeStatus.HttpStatusUnexpected, ProbeFaultCategory.ServiceUnavailable)]
    [InlineData(ProbeStatus.DnsFailure, ProbeFaultCategory.NameResolution)]
    [InlineData(ProbeStatus.ProtocolError, ProbeFaultCategory.ProtocolError)]
    [InlineData(ProbeStatus.LocalError, ProbeFaultCategory.LocalResource)]
    [InlineData(ProbeStatus.Unknown, ProbeFaultCategory.Unknown)]
    public void 分类符合预期(ProbeStatus status, ProbeFaultCategory expected)
    {
        Assert.Equal(expected, status.Category());
    }

    [Fact]
    public void 目标沉默的多种表现同属一类()
    {
        // 设备离线时 ARP 缓存在「有旧条目」与「重新解析失败」之间翻转，
        // 表现为 请求超时 ↔ 目标主机不可达；它们必须是同一类别，否则每翻转一次就是一条状态事件。
        Assert.Equal(ProbeStatus.Timeout.Category(), ProbeStatus.Unreachable.Category());
        Assert.Equal(ProbeStatus.Timeout.Category(), ProbeStatus.NoReply.Category());
    }

    [Fact]
    public void 服务不可用与目标沉默不同类()
    {
        // 端口关闭说明主机可达，与「目标整体没响应」是两件事，不能同类。
        Assert.NotEqual(ProbeStatus.Timeout.Category(), ProbeStatus.PortClosed.Category());
    }

    [Fact]
    public void 所有状态都有描述()
    {
        foreach (ProbeStatus status in Enum.GetValues<ProbeStatus>())
            Assert.False(string.IsNullOrWhiteSpace(status.ToDisplayString()));
    }

    [Fact]
    public void 中文界面下状态描述为中文()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");

            Assert.Equal("通讯正常", ProbeStatus.Success.ToDisplayString());
            Assert.Equal("请求超时", ProbeStatus.Timeout.ToDisplayString());
            Assert.Equal("端口关闭", ProbeStatus.PortClosed.ToDisplayString());
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void 英文界面下状态描述为英文()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");

            Assert.Equal("OK", ProbeStatus.Success.ToDisplayString());
            Assert.Equal("Timeout", ProbeStatus.Timeout.ToDisplayString());
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData(ProbeStatus.Timeout, true)]
    [InlineData(ProbeStatus.NoReply, true)]
    [InlineData(ProbeStatus.Unreachable, false)]
    [InlineData(ProbeStatus.PortClosed, false)]
    [InlineData(ProbeStatus.DnsFailure, false)]
    [InlineData(ProbeStatus.HttpStatusUnexpected, false)]
    [InlineData(ProbeStatus.Success, false)]
    public void 只有完全没有回应才算丢包(ProbeStatus status, bool expected)
    {
        Assert.Equal(expected, status.CountsAsLoss());
    }

    [Fact]
    public void 未知状态不算故障()
    {
        Assert.False(ProbeStatus.Unknown.IsFailure());
        Assert.False(ProbeStatus.Success.IsFailure());
        Assert.True(ProbeStatus.Timeout.IsFailure());
        Assert.True(ProbeStatus.PortClosed.IsFailure());
    }

    [Theory]
    [InlineData(ProbeStatus.Timeout, true)]
    [InlineData(ProbeStatus.Unreachable, true)]
    [InlineData(ProbeStatus.ResponseTimeout, true)]
    [InlineData(ProbeStatus.PortClosed, false)]
    [InlineData(ProbeStatus.DnsFailure, false)]
    public void 中断升级比丢包率口径更宽(ProbeStatus status, bool expected)
    {
        Assert.Equal(expected, status.CountsAsOutage());
    }

    [Theory]
    [InlineData(ProbeStatus.Success, true)]
    [InlineData(ProbeStatus.PortClosed, true)]
    [InlineData(ProbeStatus.TlsFailure, true)]
    [InlineData(ProbeStatus.HttpStatusUnexpected, true)]
    [InlineData(ProbeStatus.Timeout, false)]
    [InlineData(ProbeStatus.NoReply, false)]
    [InlineData(ProbeStatus.DnsFailure, false)]
    public void 主机给出回应即结束中断(ProbeStatus status, bool expected)
    {
        Assert.Equal(expected, status.EndsOutage());
    }

    [Fact]
    public void 协议名解析大小写不敏感()
    {
        Assert.True(ProbeProtocolExtensions.TryParseProtocol("TCP", out ProbeProtocol tcp));
        Assert.Equal(ProbeProtocol.Tcp, tcp);

        Assert.True(ProbeProtocolExtensions.TryParseProtocol(" https ", out ProbeProtocol http));
        Assert.Equal(ProbeProtocol.Http, http);

        Assert.False(ProbeProtocolExtensions.TryParseProtocol("smtp", out _));
    }
}
