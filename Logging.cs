using System.Collections.Concurrent;
using System.Text;

namespace Pings
{
    /// <summary>
    /// 日志记录器类，提供异步日志写入功能
    /// </summary>
    class Logging : IDisposable
    {
        private bool disposedValue;

        private CancellationTokenSource CTS { get; set; }
        private StreamWriter OutputFile { get; set; }
        /// <summary>日志消息队列</summary>
        private ConcurrentQueue<string>? Logs { get; set; }
        /// <summary>字符串构建器，用于批量写入</summary>
        private StringBuilder? Builder { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="output">日志文件路径</param>
        public Logging(string output)
        {
            CTS = new();
            Logs = new();
            OutputFile = new(Path.GetFullPath(output), true);
            Builder = new StringBuilder();
            Task.Run(async () =>
            {
                while (!CTS.Token.IsCancellationRequested)
                {
                    Write();
                    await Task.Delay(TimeSpan.FromSeconds(1), CTS.Token);
                }
            }, CTS.Token);
        }

        /// <summary>
        /// 将队列中的日志写入文件
        /// </summary>
        private void Write()
        {
            if (!(Logs?.IsEmpty ?? false))
            {
                while (Logs?.TryDequeue(out string? item) ?? false)
                {
                    if (item != null) Builder?.AppendLine(item);
                }
                OutputFile.Write(Builder);
                OutputFile.Flush();
                Builder?.Clear();
            }
        }

        /// <summary>
        /// 记录日志消息
        /// </summary>
        /// <param name="content">日志内容</param>
        public void Log(string content)
        {
            Logs?.Enqueue($"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss}] {content}");
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Write();
                    CTS.Cancel();
                    Builder = null;
                    Logs = null;
                    OutputFile.Dispose();
                    CTS.Dispose();
                }
                disposedValue = true;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}