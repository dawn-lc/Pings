using System.Reflection;

namespace Pings.Diagnostics
{
    /// <summary>
    /// 程序元信息。
    /// </summary>
    /// <remarks>
    /// 版本号唯一真相源是 git tag：构建时由工作流注入
    /// <c>-p:AppVersion=x.y.z -p:AppVersionType=Release</c>，csproj 把它们写进
    /// <see cref="AssemblyMetadataAttribute"/>（见 <c>Pings.csproj</c>），这里反射读回来。
    ///
    /// 刻意不读 <see cref="AssemblyInformationalVersionAttribute"/>：那是 SDK 生成的，
    /// 会追加源码哈希（<c>1.2.3+c6e9e86…</c>），解析它等于把「版本正确」寄托在
    /// SDK 内部行为上。自定义元数据由我们自己写入，契约稳定。
    ///
    /// 程序集级特性在 IL 链接器裁剪下默认保留，因此 Native AOT 下同样可用，
    /// 不需要 <c>DynamicallyAccessedMembers</c>。
    /// </remarks>
    public static class AppInfo
    {
        public const string ProductName = "Pings";

        public const string RepositoryUrl = "https://github.com/dawn-lc/Pings";

        /// <summary>正式发布渠道。其它取值（Beta / Dev …）会出现在版本文本的后缀里。</summary>
        public const string ReleaseType = "Release";

        /// <summary>构建未注入版本时使用该占位值（源码里不存在版本常量）。</summary>
        public const string UnknownVersion = "0.0.0";

        /// <summary>构建未注入类型时的占位值，同时也是本地开发构建的标记。</summary>
        public const string DevelopmentType = "Dev";

        /// <summary>语义化版本号（<c>x.y.z</c>）。</summary>
        public static string Version { get; } = ReadMetadata("AppVersion") ?? UnknownVersion;

        /// <summary>发布类型：Release / Beta / Dev …</summary>
        public static string VersionType { get; } = ReadMetadata("AppVersionType") ?? DevelopmentType;

        /// <summary>是否本地开发构建（依据显式标记，而不是「版本号是不是 0」）。</summary>
        public static bool IsDevelopment
            => string.Equals(VersionType, DevelopmentType, StringComparison.OrdinalIgnoreCase);

        /// <summary>是否正式发布渠道。</summary>
        public static bool IsRelease
            => string.Equals(VersionType, ReleaseType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 界面 / 日志用的版本文本。Release 不带后缀，其它类型带 <c>-类型</c>，
        /// 这样 beta 包与正式包在 <c>--version</c>、日志、窗口标题里都能分辨。
        /// </summary>
        public static string VersionText => IsRelease
            ? $"{ProductName} {Version}"
            : $"{ProductName} {Version}-{VersionType}";

        /// <summary>
        /// 按指定类型拼出版本文本（供测试与工作流断言复用同一份规则）。
        /// </summary>
        public static string FormatVersionText(string version, string versionType)
        {
            string suffix = string.Equals(versionType, ReleaseType, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : "-" + versionType;

            return $"{ProductName} {version}{suffix}";
        }

        /// <summary>
        /// 读取自定义程序集元数据。
        /// </summary>
        /// <remarks>
        /// 用 <c>typeof(AppInfo).Assembly</c> 而非 <c>Assembly.GetEntryAssembly()</c>：
        /// 后者在单元测试里拿到的是 testhost，读到的是无关程序集的元数据。
        /// </remarks>
        private static string? ReadMetadata(string key)
        {
            string? value = typeof(AppInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
                ?.Value;

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
