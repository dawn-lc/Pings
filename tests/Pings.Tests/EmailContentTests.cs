using System.Globalization;
using Pings.Configuration;
using Pings.Diagnostics;
using Pings.Monitoring;
using Pings.Notifications;

namespace Pings.Tests;

/// <summary>
/// 邮件内容生成：模板渲染、内置模板与转义。全过程不接触 SMTP。
/// </summary>
public class EmailContentTests
{
    private static ProbeTaskSnapshot Snapshot(
        ProbeStatus status = ProbeStatus.Timeout,
        ProbeSeverity severity = ProbeSeverity.Warning,
        string? detail = null)
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

        return new ProbeTaskSnapshot(
            "网关",
            ProbeProtocol.Icmp,
            "192.168.1.1",
            "icmp://192.168.1.1",
            status,
            detail,
            status.Category(),
            severity,
            status.IsSuccess() ? 12 : null,
            null,
            12,
            statistics,
            new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero),
            "请求超时",
            null,
            0,
            0);
    }

    [Fact]
    public void 未配置模板时主题与正文使用内置文案()
    {
        using CultureScope chinese = CultureScope.Chinese();

        EmailContent content = EmailContent.Create(new EmailConfig(), Snapshot());

        Assert.Equal("[Pings] 网关 (icmp://192.168.1.1) 状态：请求超时", content.Subject);
        Assert.Contains("任务名称：网关", content.TextBody);
        Assert.Contains("丢包率(窗口 10 次)：50.0%", content.TextBody);
        Assert.Contains("累计丢包率：20.0%", content.TextBody);
        Assert.Contains("发生时间：2026-11-02 09:30:00", content.TextBody);
        Assert.Contains(AppInfo.VersionText, content.TextBody);
    }

    [Fact]
    public void 内置文案跟随界面语言()
    {
        using CultureScope english = CultureScope.English();

        EmailContent content = EmailContent.Create(new EmailConfig(), Snapshot());

        Assert.Equal("[Pings] 网关 (icmp://192.168.1.1) status: Timeout", content.Subject);
        Assert.Contains("Task: 网关", content.TextBody);
        Assert.Contains("Total loss rate: 20.0%", content.TextBody);
    }

    [Fact]
    public void 内置HTML按严重程度着色且转义用户内容()
    {
        using CultureScope chinese = CultureScope.Chinese();

        string warning = EmailContent.Create(new EmailConfig(), Snapshot()).HtmlBody;
        string critical = EmailContent.Create(new EmailConfig(), Snapshot(ProbeStatus.PortClosed, ProbeSeverity.Critical)).HtmlBody;
        string recovered = EmailContent.Create(new EmailConfig(), Snapshot(ProbeStatus.Success, ProbeSeverity.None)).HtmlBody;

        Assert.Contains("#dc6803", warning);
        Assert.Contains("#d92d20", critical);
        Assert.Contains("#12b76a", recovered);
        Assert.Contains("2026-11-02 09:30:00", warning);
    }

    [Fact]
    public void 自定义模板按变量表渲染()
    {
        using CultureScope chinese = CultureScope.Chinese();

        EmailConfig config = new()
        {
            Subject = "%#Name#% / %#State#%",
            Content = "目标：%#Endpoint#%\r\n丢包：%#LossRate#%%"
        };

        EmailContent content = EmailContent.Create(config, Snapshot());

        Assert.Equal("网关 / 请求超时", content.Subject);
        Assert.Equal("目标：icmp://192.168.1.1\r\n丢包：50.0%", content.TextBody);
    }

    [Fact]
    public void 只配纯文本模板时HTML由它转义生成()
    {
        using CultureScope chinese = CultureScope.Chinese();

        EmailConfig config = new()
        {
            Content = "<b>%#Name#%</b>\r\n第二行"
        };

        EmailContent content = EmailContent.Create(config, Snapshot());

        Assert.Contains("&lt;b&gt;网关&lt;/b&gt;", content.HtmlBody);
        Assert.Contains("<br>", content.HtmlBody);
        Assert.DoesNotContain("<b>", content.HtmlBody);

        // 用户自己写了正文，就不该再套内置的告警字段表。
        Assert.DoesNotContain("累计丢包率", content.HtmlBody);
    }

    [Fact]
    public void 自定义HTML模板优先于纯文本生成()
    {
        EmailConfig config = new()
        {
            Content = "纯文本：%#Name#%",
            HtmlContent = "<p style=\"color:red\">%#Name#% - %#Status#%</p>"
        };

        EmailContent content = EmailContent.Create(config, Snapshot());

        Assert.Equal("纯文本：网关", content.TextBody);
        Assert.Equal("<p style=\"color:red\">网关 - Timeout</p>", content.HtmlBody);
    }

    [Fact]
    public void 未识别的占位符原样保留()
    {
        EmailConfig config = new() { Subject = "%#Name#% %#NotAVariable#%" };

        EmailContent content = EmailContent.Create(config, Snapshot());

        Assert.Equal("网关 %#NotAVariable#%", content.Subject);
    }

    [Fact]
    public void 百分比变量不受当前区域性影响()
    {
        // decimal comma 的文化：若变量表用了 CurrentCulture，这里会渲染成 50,0 而不是 50.0。
        using CultureScope chinese = CultureScope.Chinese();
        string previous = CultureInfo.CurrentCulture.Name;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            EmailContent content = EmailContent.Create(new EmailConfig { Content = "%#LossRate#%" }, Snapshot());

            Assert.Equal("50.0", content.TextBody);
        }
        finally
        {
            CultureInfo.CurrentCulture = new CultureInfo(previous);
        }
    }
}
