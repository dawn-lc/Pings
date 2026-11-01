using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pings.Tests;

/// <summary>
/// 把整个测试运行的默认界面语言固定为 <c>en-US</c>。
/// </summary>
/// <remarks>
/// 为什么必须是英文而不是中文：CI 的 runner 是英文环境，而开发机通常是中文。
/// 若默认跟随机器，那么「断言中文文案」的用例在本地绿、CI 红——这类失败只会在
/// 打 tag 之后才暴露（v1.0.22 第一次发布就是这么挂的）。
///
/// 固定成英文之后：
/// <list type="bullet">
/// <item>漏了文化固定的用例在本地就会红，不必等 CI；</item>
/// <item>要断言中文的用例必须显式开 <see cref="CultureScope.Chinese" /> 作用域，
///   把「这条用例依赖哪种语言」写在代码里，而不是依赖跑测的人是谁。</item>
/// </list>
///
/// 同时设置 <c>DefaultThreadCurrentUICulture</c>：xUnit 在多个线程池线程上并行跑用例，
/// 只改当前线程的文化覆盖不到它们。
/// </remarks>
internal static class TestCultureDefaults
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        CultureInfo english = new("en-US");

        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
    }
}
