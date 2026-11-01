using System.Text;
using Pings.Properties;

namespace Pings.Monitoring
{
    /// <summary>
    /// UDP 探测负载的来源解析：纯文本、Base64 文本或文件。
    /// </summary>
    /// <remarks>
    /// 配置里的 <c>udpPayload</c> 支持三种写法，按下列顺序判定：
    /// <list type="number">
    /// <item><c>base64:&lt;数据&gt;</c> —— 解码成二进制，用于发固定字节序列（例如硬件协议、心跳包）；</item>
    /// <item><c>file:&lt;路径&gt;</c> —— 原样读取文件内容，适合二进制报文或需要外部维护的负载；</item>
    /// <item>其他值 —— 若该值指向一个**存在的文件**，按文件读；否则按 UTF-8 文本发送。</item>
    /// </list>
    ///
    /// 第三条是刻意的便利：用户直觉上会把路径直接写进 <c>udpPayload</c>，不必记住前缀。
    /// 代价是「同名文件恰好存在」时会被当成路径——需要发送这种文本时加 <c>base64:</c>
    /// 或换个措辞即可。相对路径一律相对**配置文件所在目录**解析，
    /// 与 <c>logging.logFilePath</c> 的约定一致，不受启动时工作目录影响。
    /// </remarks>
    public static class UdpPayloadResolver
    {
        /// <summary>Base64 形式的前缀。</summary>
        public const string Base64Prefix = "base64:";

        /// <summary>文件形式的前缀。</summary>
        public const string FilePrefix = "file:";

        /// <summary>UDP 负载上限（IPv4 最大数据报 65507 字节）。</summary>
        public const int MaxBytes = 65_507;

        /// <summary>默认负载。</summary>
        public const string DefaultText = "Pings";

        /// <summary>
        /// 解析负载。<paramref name="spec" /> 为空时返回默认文本。
        /// </summary>
        /// <param name="spec">配置里的原始值。</param>
        /// <param name="baseDirectory">相对路径的基准目录；为 null 时用当前工作目录。</param>
        /// <param name="bytes">解析出的字节。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否成功。</returns>
        public static bool TryResolve(string? spec, string? baseDirectory, out byte[] bytes, out string? error)
        {
            string value = spec?.Trim() ?? string.Empty;

            if (value.Length == 0)
            {
                bytes = Encoding.UTF8.GetBytes(DefaultText);
                error = null;
                return true;
            }

            if (TryStripPrefix(value, Base64Prefix, out string base64))
                return TryDecodeBase64(base64, out bytes, out error);

            if (TryStripPrefix(value, FilePrefix, out string explicitPath))
                return TryReadFile(explicitPath, baseDirectory, out bytes, out error);

            // 无前缀：只有文件真的存在才按路径处理，否则当文本——避免把普通文本误判成路径。
            string candidate = ResolvePath(value, baseDirectory);
            if (File.Exists(candidate))
                return TryReadFile(value, baseDirectory, out bytes, out error);

            return TryText(value, out bytes, out error);
        }

        /// <summary>
        /// 只做检查、不返回字节，供配置校验使用（避免校验时把大文件读进内存）。
        /// </summary>
        public static bool TryValidate(string? spec, string? baseDirectory, out string? error)
            => TryResolve(spec, baseDirectory, out _, out error);

        private static bool TryStripPrefix(string value, string prefix, out string remainder)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                remainder = value[prefix.Length..].Trim();
                return true;
            }

            remainder = string.Empty;
            return false;
        }

        private static bool TryDecodeBase64(string data, out byte[] bytes, out string? error)
        {
            bytes = [];

            if (data.Length == 0)
            {
                error = Resources.UdpBase64Empty;
                return false;
            }

            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                error = Resources.UdpBase64Invalid;
                return false;
            }

            if (bytes.Length == 0)
            {
                error = Resources.UdpPayloadEmptyAfterDecode;
                return false;
            }

            if (bytes.Length > MaxBytes)
            {
                error = string.Format(Resources.UdpPayloadTooLarge, bytes.Length, MaxBytes);
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryReadFile(string path, string? baseDirectory, out byte[] bytes, out string? error)
        {
            bytes = [];

            if (path.Length == 0)
            {
                error = Resources.UdpFilePrefixNeedsPath;
                return false;
            }

            string fullPath = ResolvePath(path, baseDirectory);

            if (!File.Exists(fullPath))
            {
                error = string.Format(Resources.UdpFileNotFound, fullPath);
                return false;
            }

            try
            {
                FileInfo info = new(fullPath);

                if (info.Length > MaxBytes)
                {
                    error = string.Format(Resources.UdpFileTooLarge, fullPath, info.Length, MaxBytes);
                    return false;
                }

                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = string.Format(Resources.UdpFileReadFailed, ex.Message);
                return false;
            }

            if (bytes.Length == 0)
            {
                error = string.Format(Resources.UdpFileEmpty, fullPath);
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryText(string value, out byte[] bytes, out string? error)
        {
            bytes = Encoding.UTF8.GetBytes(value);

            if (bytes.Length > MaxBytes)
            {
                error = string.Format(Resources.UdpPayloadTooLarge, bytes.Length, MaxBytes);
                return false;
            }

            error = null;
            return true;
        }

        private static string ResolvePath(string path, string? baseDirectory)
        {
            string trimmed = path.Trim();

            if (Path.IsPathRooted(trimmed))
                return Path.GetFullPath(trimmed);

            string root = string.IsNullOrEmpty(baseDirectory)
                ? Directory.GetCurrentDirectory()
                : baseDirectory;

            return Path.GetFullPath(Path.Combine(root, trimmed));
        }
    }
}
