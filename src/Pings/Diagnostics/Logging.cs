using System.Threading.Channels;
using Pings.Configuration;
using Pings.Properties;

namespace Pings.Diagnostics
{
    /// <summary>
    /// 异步日志记录器：调用方只往无锁通道写入，由单一后台任务落盘，
    /// 保证 <see cref="RotatingLogFile"/> 只有一个写者，不会出现并发写坏文件。
    /// </summary>
    public sealed class Logging : IDisposable
    {
        private readonly Channel<string> _channel;
        private readonly RotatingLogFile? _file;
        private readonly bool _consoleEnabled;
        private readonly Task _writerLoop;
        private int _disposed;

        public Logging(LoggingConfig? config = null)
        {
            config ??= new LoggingConfig();

            _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _consoleEnabled = config.EnableConsoleLog;

            try
            {
                long maxBytes = Math.Max(1, config.MaxLogFileSize) * 1024L * 1024L;
                _file = new RotatingLogFile(config.LogFilePath, maxBytes, Math.Max(1, config.MaxLogFiles));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 日志文件不可用时退化为仅控制台输出，而不是让程序启动失败。
                Console.Error.WriteLine(string.Format(Resources.LogFileUnavailable, ex.Message));
                _file = null;
            }

            _writerLoop = Task.Run(WriterLoopAsync);
        }

        /// <summary>实时表格渲染期间挂起控制台输出，避免破坏 Live 布局。</summary>
        public bool SuppressConsole { get; set; }

        public string? FilePath => _file?.FilePath;

        public void Log(string message)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            string line = $"[{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss}] {message}";
            _channel.Writer.TryWrite(line);

            if (!_consoleEnabled || SuppressConsole)
                return;

            try
            {
                Console.WriteLine(line);
            }
            catch (IOException)
            {
                // 控制台/管道已关闭时不能因为日志输出异常中断监控。
            }
        }

        private async Task WriterLoopAsync()
        {
            try
            {
                await foreach (string line in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
                    _file?.WriteLine(line);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Format(Resources.LogWriterCrashed, ex.Message));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _channel.Writer.TryComplete();

            try
            {
                // 给后台任务一点时间把队列写完，避免退出时丢日志。
                _writerLoop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            _file?.Dispose();
        }
    }
}
