// 构建期 MSBuild 内联任务：由 csproj 的 <UsingTask Code Source="..."> 引用本文件。
//
// 为什么放在独立 .cs 而不是 csproj 的 CDATA 块里：
//   ① 有语法高亮/补全/重构，改错在编辑器里就能发现；
//   ② 不受 XML 转义限制（不必考虑 && / < / ]]> 截断）；
//   ③ build/ 在仓库根、不在任何项目的 **/*.cs 通配范围内，不会被编进程序集。
//
// 注意：RoslynCodeTaskFactory 的编译参考集较旧 —— 没有 ProcessStartInfo.ArgumentList，
// 也没有部分 .NET Core 才有的重载。这里只用该参考集里确定存在的 API。
//
// 命名空间刻意留空：RoslynCodeTaskFactory 按 TaskName 全名匹配类型。

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>与具体工具无关的网络 / 文本辅助。</summary>
public static class BuildTaskHelpers
{
    /// <summary>
    /// 下载与探测的重试次数。
    /// <para>
    /// GitHub 的 TLS 握手会被间歇性掐断，实测能持续数十秒；固定短间隔重试毫无意义，
    /// 因此用指数退避（2、4、8 秒，总退避 14 秒）。失败重试的代价远小于
    /// 「产物静默没压缩/没内嵌」。
    /// </para>
    /// </summary>
    public const int DownloadAttempts = 4;

    private const string UserAgent = "Pings-build";

    /// <summary>
    /// 强制启用 TLS 1.2。GitHub 只接受 TLS 1.2+，而较旧宿主默认协议可能是 TLS 1.0，
    /// 表现为 "SSL connection could not be established"。
    /// 用数值 3072 而非枚举名，避免依赖参考程序集是否定义了该成员。
    /// </summary>
    public static void EnsureTls12()
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
        }
        catch
        {
            // 受限环境不允许改全局协议：交给后续请求自行报错
        }
    }

    public static HttpClient CreateClient(TimeSpan timeout)
    {
        HttpClient client = new() { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>跟随 releases/latest 的 302 重定向读取 tag，避免消耗 GitHub API 配额（未认证仅 60 次/小时）。</summary>
    public static string ResolveLatestVersion(string releasesLatestUrl, Action<string> log)
    {
        using HttpClientHandler handler = new() { AllowAutoRedirect = false };
        using HttpClient probe = new(handler) { Timeout = TimeSpan.FromSeconds(30) };
        probe.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        using HttpResponseMessage response = GetResponseWithRetry(probe, releasesLatestUrl, log);
        Uri location = response.Headers.Location;
        if (location is null)
            return null;

        // .../releases/tag/v5.2.1 → 5.2.1
        string tag = location.Segments.LastOrDefault();
        return string.IsNullOrEmpty(tag) ? null : tag.TrimStart('v', 'V');
    }

    public static HttpResponseMessage GetResponseWithRetry(HttpClient client, string url, Action<string> log)
        => Retry(() => client.GetAsync(url).GetAwaiter().GetResult(),
            "请求失败（已重试 " + DownloadAttempts + " 次）：" + url, log);

    public static byte[] GetBytesWithRetry(HttpClient client, string url, Action<string> log)
        => Retry(() => client.GetByteArrayAsync(url).GetAwaiter().GetResult(),
            "下载失败（已重试 " + DownloadAttempts + " 次）：" + url, log);

    private static T Retry<T>(Func<T> action, string failureMessage, Action<string> log)
    {
        Exception last = null;

        for (int attempt = 1; attempt <= DownloadAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                last = ex;

                if (attempt < DownloadAttempts)
                {
                    int delaySeconds = 1 << attempt; // 2、4、8 秒
                    log?.Invoke($"第 {attempt}/{DownloadAttempts} 次尝试失败（{Describe(ex)}），{delaySeconds} 秒后重试…");
                    Thread.Sleep(delaySeconds * 1000);
                }
            }
        }

        throw new InvalidOperationException(failureMessage, last);
    }

    /// <summary>
    /// 展开完整异常链。HttpRequestException 的 Message 往往只有一句
    /// "The SSL connection could not be established"，真正原因全在 InnerException 里。
    /// </summary>
    public static string Describe(Exception ex)
    {
        StringBuilder builder = new();

        for (Exception current = ex; current is not null; current = current.InnerException)
        {
            if (builder.Length > 0)
                builder.Append(" <- ");

            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }

        return builder.ToString();
    }

    /// <summary>读取 .version 标记（不存在时返回 null）</summary>
    public static string ReadVersionMarker(string versionFile)
        => File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;

    /// <summary>
    /// 请求的是否为固定版本。null / 空 / "latest" 都视为「跟随最新」。
    /// 会归一化掉 v 前缀：缓存里 .version 存的永远是不带 v 的形式。
    /// </summary>
    public static bool IsPinned(string requestedVersion, out string pinned)
    {
        pinned = string.IsNullOrEmpty(requestedVersion)
            ? requestedVersion
            : requestedVersion.Trim().TrimStart('v', 'V');

        return !string.IsNullOrEmpty(pinned)
            && !pinned.Equals("latest", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>给路径加引号（内联任务没有 ProcessStartInfo.ArgumentList，只能手工拼）</summary>
    public static string Quote(string value) => "\"" + value + "\"";

    /// <summary>
    /// 宿主是否为 Windows。用目录分隔符判断而不用 <c>RuntimeInformation</c>：RoslynCodeTaskFactory 的参考集较旧，
    /// 少一个 API 依赖就少一处跨 SDK 版本的风险。
    /// </summary>
    public static bool HostIsWindows => Path.DirectorySeparatorChar == '\\';

    /// <summary>在 PATH 里找可执行文件；找不到返回 null。</summary>
    public static string FindOnPath(string fileName)
    {
        string path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (string entry in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(entry.Trim(), fileName));
            }
            catch
            {
                // PATH 里的无效项直接跳过，不该让构建失败
                continue;
            }

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>从 zip 中解出指定文件名的条目；找不到返回 false</summary>
    public static bool TryExtractFromZip(byte[] zipBytes, string fileName, string destination)
    {
        using ZipArchive archive = new(new MemoryStream(zipBytes), ZipArchiveMode.Read);

        ZipArchiveEntry entry = archive.Entries.FirstOrDefault(
            e => string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return false;

        entry.ExtractToFile(destination, overwrite: true);
        return true;
    }
}

/// <summary>
/// 解析 upx：显式路径 → 约定位置缓存（tools/upx/）→ PATH（仅非 Windows 宿主）→ 自动下载最新 win64 版。
/// <para>
/// 与「图标」那种必需件不同，<b>找不到 UPX 不算构建失败</b>：它只影响产物体积，
/// 没有它程序照样能跑。因此所有失败路径都返回 true 并只发 warning。
/// </para>
/// <para>
/// 自动下载只在 Windows 宿主上做：官方只提供 win64 的 zip，Linux / macOS 是 tar.bz2，
/// .NET 没有内置 bz2 解压，为它引入外部 tar 依赖不划算 —— 这两个平台用包管理器装一行就够。
/// </para>
/// </summary>
public class EnsureUpxToolTask : Task
{
    public string ExplicitPath { get; set; }
    public string ToolDirectory { get; set; }
    public string RequestedVersion { get; set; }
    public bool AutoDownload { get; set; }
    public bool ForceUpdate { get; set; }

    [Output] public string ResultPath { get; set; }
    [Output] public string ResolvedVersion { get; set; }
    [Output] public bool WasDownloaded { get; set; }

    private const string ReleasesLatest = "https://github.com/upx/upx/releases/latest";
    private const string ReleaseBase = "https://github.com/upx/upx/releases/download/";

    /// <summary>
    /// 重试时报告原因与等待时长。
    /// <para>
    /// 直接用 <c>Console.Error</c> 而不是 <c>Log.LogMessage</c>：实测 dotnet build 的默认
    /// verbosity 不显示任务发的 High/Normal 消息，而重试不属于错误、不该抬高警告计数。
    /// 不写出来就是一段没有任何输出的长时间等待，无法判断是卡死还是在重试。
    /// </para>
    /// </summary>
    private void RetryLog(string message) => Console.Error.WriteLine(message);

    public override bool Execute()
    {
        WasDownloaded = false;

        if (!string.IsNullOrEmpty(ToolDirectory))
            ToolDirectory = Path.GetFullPath(ToolDirectory);

        // 1) 显式指定优先
        if (!string.IsNullOrEmpty(ExplicitPath))
        {
            string explicitFull = Path.GetFullPath(ExplicitPath);

            if (File.Exists(explicitFull))
            {
                ResultPath = explicitFull;
                Log.LogMessage(MessageImportance.High, "使用指定的 UPX：" + explicitFull);
                return true;
            }

            Log.LogError("UpxPath 指向的文件不存在：" + explicitFull);
            return false;
        }

        string cachedExe = Path.Combine(ToolDirectory, BuildTaskHelpers.HostIsWindows ? "upx.exe" : "upx");
        string versionFile = Path.Combine(ToolDirectory, ".version");
        string cachedVersion = BuildTaskHelpers.ReadVersionMarker(versionFile);
        bool wantPinned = BuildTaskHelpers.IsPinned(RequestedVersion, out string pinned);

        // 2) 缓存命中（跟随最新时视缓存为最新；固定版本时须一致）
        if (!ForceUpdate && File.Exists(cachedExe))
        {
            // 非 Windows 宿主的缓存无法自愈（不做自动下载），没有版本标记时按可用处理：
            // 否则「手工放入 tools/upx/upx」这种最自然的做法会被判成版本不符而静默跳过压缩。
            bool unverifiable = cachedVersion is null && !BuildTaskHelpers.HostIsWindows;

            if (!wantPinned || unverifiable
                || string.Equals(cachedVersion, pinned, StringComparison.OrdinalIgnoreCase))
            {
                ResultPath = cachedExe;
                ResolvedVersion = cachedVersion;
                Log.LogMessage(MessageImportance.High,
                    $"使用已缓存的 UPX {cachedVersion ?? "(版本未知)"}：{cachedExe}");
                return true;
            }

            Log.LogMessage(MessageImportance.High,
                $"缓存版本 {cachedVersion ?? "?"} 不符合请求的 {pinned}，重新获取。");
        }

        if (!AutoDownload)
        {
            Log.LogWarning("未找到 UPX 且已禁用自动下载（UpxAutoDownload=false），跳过压缩。");
            return true;
        }

        // 非 Windows 宿主不做自动下载（官方包是 tar.bz2，解压要外部 tar）：
        // 包管理器装上就够，找到即用。
        if (!BuildTaskHelpers.HostIsWindows)
        {
            string onPath = BuildTaskHelpers.FindOnPath("upx");

            if (!string.IsNullOrEmpty(onPath))
            {
                ResultPath = onPath;
                Log.LogMessage(MessageImportance.High, "使用 PATH 中的 UPX：" + onPath);
                return true;
            }

            Log.LogWarning("未找到 UPX，跳过压缩。安装（apt install upx-ucl / brew install upx）"
                + " 或把可执行文件放到 tools/upx/upx，也可用 -p:UpxPath=... 显式指定。");
            return true;
        }

        // 3) 下载
        try
        {
            BuildTaskHelpers.EnsureTls12();

            using HttpClient http = BuildTaskHelpers.CreateClient(TimeSpan.FromMinutes(5));

            string version = wantPinned
                ? pinned
                : BuildTaskHelpers.ResolveLatestVersion(ReleasesLatest, RetryLog);

            if (string.IsNullOrEmpty(version))
            {
                Log.LogWarning("无法确定 UPX 最新版本，跳过压缩。可改用 -p:UpxVersion=x.y.z 固定版本。");
                return true;
            }

            string zipUrl = ReleaseBase + "v" + version + "/upx-" + version + "-win64.zip";
            Log.LogMessage(MessageImportance.High, $"下载 UPX {version}：{zipUrl}");

            byte[] payload = BuildTaskHelpers.GetBytesWithRetry(http, zipUrl, RetryLog);

            Directory.CreateDirectory(ToolDirectory);

            if (!BuildTaskHelpers.TryExtractFromZip(payload, "upx.exe", cachedExe))
            {
                Log.LogWarning("UPX 压缩包内未找到 upx.exe，跳过压缩。");
                return true;
            }

            File.WriteAllText(versionFile, version);

            ResultPath = cachedExe;
            ResolvedVersion = version;
            WasDownloaded = true;
            Log.LogMessage(MessageImportance.High, $"UPX {version} 已就绪：{cachedExe}");
            return true;
        }
        catch (Exception ex)
        {
            // 网络失败不应让发布失败：UPX 只是体积优化，没有它产物依然可运行。
            Log.LogWarning("自动获取 UPX 失败（" + BuildTaskHelpers.Describe(ex) + "），跳过压缩。"
                + " 可手工把 upx.exe 放到 tools\\upx\\ 后重试。");
            return true;
        }
    }
}
