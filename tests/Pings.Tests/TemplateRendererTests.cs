using Pings.Notifications;

namespace Pings.Tests;

public class TemplateRendererTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["Name"] = "网关",
        ["IP"] = "192.168.1.1",
        ["Delay"] = "12",
        ["CurrentTime"] = "2026-11-01 08:30:00"
    };

    [Fact]
    public void 替换基本占位符()
    {
        string result = TemplateRenderer.Render("%#Name#% (%#IP#%)", Values);

        Assert.Equal("网关 (192.168.1.1)", result);
    }

    [Fact]
    public void 同一占位符出现多次都替换()
    {
        string result = TemplateRenderer.Render("%#Delay#%/%#Delay#%", Values);

        Assert.Equal("12/12", result);
    }

    [Fact]
    public void 未命中的占位符原样保留()
    {
        string result = TemplateRenderer.Render("%#Unknown#%", Values);

        Assert.Equal("%#Unknown#%", result);
    }

    [Fact]
    public void 支持时间格式化()
    {
        string result = TemplateRenderer.Render("%#CurrentTime:yyyy/MM/dd#%", Values);

        Assert.Equal("2026/11/01", result);
    }

    [Fact]
    public void 非法时间格式退回原值()
    {
        // 未闭合的单引号会抛 FormatException，用来验证「格式串笔误不丢通知内容」。
        string result = TemplateRenderer.Render("%#CurrentTime:'unclosed#%", Values);

        Assert.Equal("2026-11-01 08:30:00", result);
    }

    [Fact]
    public void 缺少后缀时保留剩余文本()
    {
        string result = TemplateRenderer.Render("前 %#Name", Values);

        Assert.Equal("前 %#Name", result);
    }

    [Fact]
    public void 空模板返回空串()
    {
        Assert.Equal(string.Empty, TemplateRenderer.Render(null, Values));
        Assert.Equal(string.Empty, TemplateRenderer.Render("", Values));
    }

    [Fact]
    public void 占位符包含自身不会死循环()
    {
        Dictionary<string, string> values = new() { ["A"] = "%#A#%" };

        string result = TemplateRenderer.Render("%#A#%", values);

        Assert.Equal("%#A#%", result);
    }
}
