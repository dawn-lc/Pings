using System.Text;

namespace Pings.Diagnostics
{
    /// <summary>
    /// 带轮转的日志文件写入器。
    /// </summary>
    /// <remarks>
    /// 轮转规则：主文件达到体积上限后，<c>Pings.log</c> → <c>Pings.1.log</c>，
    /// 原 <c>Pings.1.log</c> → <c>Pings.2.log</c> …… 最旧的一份被删除。
    /// 单写者使用（由 <see cref="Logging"/> 的后台任务驱动）。
    /// </remarks>
    public sealed class RotatingLogFile : IDisposable
    {
        private readonly string _fullPath;
        private readonly long _maxBytes;
        private readonly int _maxFiles;
        private StreamWriter? _writer;
        private long _bytesWritten;

        public RotatingLogFile(string path, long maxBytes, int maxFiles)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("日志路径不能为空", nameof(path));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (maxFiles < 1)
                throw new ArgumentOutOfRangeException(nameof(maxFiles));

            _fullPath = Path.GetFullPath(path);
            _maxBytes = maxBytes;
            _maxFiles = maxFiles;

            string? directory = Path.GetDirectoryName(_fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            Open();
        }

        public string FilePath => _fullPath;

        public void WriteLine(string line)
        {
            StreamWriter? writer = _writer;
            if (writer is null)
                return;

            try
            {
                long bytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

                if (_bytesWritten > 0 && _bytesWritten + bytes > _maxBytes)
                    Rotate();

                _writer?.WriteLine(line);
                _bytesWritten += bytes;
            }
            catch (IOException)
            {
                // 日志写失败不能影响监控主流程；关闭写入器避免反复异常。
                CloseWriter();
            }
            catch (UnauthorizedAccessException)
            {
                CloseWriter();
            }
        }

        private void Open()
        {
            FileStream stream = new(_fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _bytesWritten = stream.Length;
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        private void Rotate()
        {
            CloseWriter();

            try
            {
                string oldest = ArchivePath(_maxFiles);
                if (File.Exists(oldest))
                    File.Delete(oldest);

                for (int index = _maxFiles - 1; index >= 1; index--)
                {
                    string source = ArchivePath(index);
                    if (File.Exists(source))
                        File.Move(source, ArchivePath(index + 1), overwrite: true);
                }

                if (File.Exists(_fullPath))
                    File.Move(_fullPath, ArchivePath(1), overwrite: true);
            }
            catch (IOException)
            {
                // 轮转失败时继续写主文件，不能因此中断日志。
            }
            catch (UnauthorizedAccessException)
            {
            }

            try
            {
                Open();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 打不开新文件时保持 writer 为 null，后续写入会被忽略而不是抛异常。
            }
        }

        private string ArchivePath(int index)
        {
            string directory = Path.GetDirectoryName(_fullPath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(_fullPath);
            string extension = Path.GetExtension(_fullPath);
            return Path.Combine(directory, $"{name}.{index}{extension}");
        }

        private void CloseWriter()
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch (IOException)
            {
            }
            finally
            {
                _writer = null;
            }
        }

        public void Dispose()
        {
            CloseWriter();
        }
    }
}
