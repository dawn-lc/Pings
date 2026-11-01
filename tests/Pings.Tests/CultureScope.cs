using System.Globalization;

namespace Pings.Tests;

/// <summary>
/// 临时切换界面语言的测试作用域，退出时还原。
/// </summary>
/// <remarks>
/// 校验器与各文案都是一次性从 <c>Resources</c> 取当前语言的文本，
/// 因此断言中文字面量的测试必须先固定 <see cref="CultureInfo.CurrentUICulture" />，
/// 否则在英文环境（CI）里会全挂。
///
/// 不写成 xUnit 的 collection fixture：culture 是随执行上下文流动的，
/// 每个用例自己开作用域才能保证互不影响。
/// </remarks>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;

    private CultureScope(string culture) => CultureInfo.CurrentUICulture = new CultureInfo(culture);

    /// <summary>切到简体中文。</summary>
    public static CultureScope Chinese() => new("zh-CN");

    /// <summary>切到英文（中性资源）。</summary>
    public static CultureScope English() => new("en");

    public void Dispose() => CultureInfo.CurrentUICulture = _original;
}
