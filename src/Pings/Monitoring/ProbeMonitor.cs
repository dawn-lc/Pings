using Pings.Configuration;
using Pings.Diagnostics;
using Pings.Monitoring.Transports;
using Pings.Notifications;

namespace Pings.Monitoring
{
    /// <summary>
    /// 监控编排器：持有所有任务，把任务事件落到「日志 / 告警队列 / 通知」，
    /// 不参与任何界面渲染（渲染由 <see cref="Pings.Ui.MonitorView" /> 在刷新时读取快照）。
    /// </summary>
    public sealed class ProbeMonitor : IAsyncDisposable
    {
        private const int NotificationKeyLimit = 1024;
        private static readonly TimeSpan NotificationKeyRetention = TimeSpan.FromHours(24);

        private readonly Logging _logging;
        private readonly NotificationService? _notifier;
        private readonly GlobalConfig _global;
        private readonly List<ProbeTask> _tasks = [];
        private readonly Dictionary<string, DateTimeOffset> _lastNotification = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _notificationGate = new();
        private readonly List<Task> _pendingNotifications = [];
        private bool _disposed;

        public ProbeMonitor(Logging logging, NotificationService? notifier, GlobalConfig? global = null)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _notifier = notifier;
            _global = global ?? new GlobalConfig();
        }

        public ProbeTask Add(ProbeTaskOptions options)
            => Add(new ProbeTask(options, ProbeTransportFactory.Create(options)));

        /// <summary>注册一个已构造好的任务（测试里用于注入假传输层）。</summary>
        public ProbeTask Add(ProbeTask task)
        {
            ArgumentNullException.ThrowIfNull(task);

            task.EventRaised += OnTaskEvent;
            _tasks.Add(task);
            return task;
        }

        public void StartAll(CancellationToken token = default)
        {
            foreach (ProbeTask task in _tasks)
                task.Start(token);
        }

        /// <summary>每个任务执行一次探测（--once 模式）。</summary>
        public async Task ProbeAllOnceAsync(CancellationToken token = default)
        {
            await Task.WhenAll(_tasks.Select(task => task.ProbeOnceAsync(token))).ConfigureAwait(false);
        }

        /// <summary>采集所有任务的只读快照，供界面一次性渲染。</summary>
        public IReadOnlyList<ProbeTaskSnapshot> Snapshots()
            => _tasks.Select(task => task.Snapshot()).ToArray();

        /// <summary>确认（消费）每个任务的队头警告，对应快捷键 C。</summary>
        public void ConfirmAllWarnings()
        {
            foreach (ProbeTask task in _tasks)
            {
                string? warning = task.Warnings.Dequeue();
                if (warning is not null)
                    _logging.Log($"{task.Options.Name}({task.Options.Endpoint}) 已确认警告: {warning}");
            }
        }

        public async Task StopAsync()
        {
            await Task.WhenAll(_tasks.Select(task => task.StopAsync())).ConfigureAwait(false);

            Task[] pending;
            lock (_notificationGate)
                pending = [.. _pendingNotifications];

            if (pending.Length > 0)
            {
                try
                {
                    await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logging.Log("仍有通知在发送中，已跳过等待");
                }
            }
        }

        private void OnTaskEvent(object? sender, ProbeTaskEventArgs e)
        {
            if (sender is not ProbeTask task)
                return;

            ProbeTaskSnapshot snapshot = e.Snapshot;
            string prefix = $"{snapshot.Name}({snapshot.Endpoint})";

            switch (e.Event.Kind)
            {
                case ProbeEventKind.StatusChanged:
                    _logging.Log($"{prefix} {e.Event.Message}");
                    task.Warnings.Enqueue($"{snapshot.StatusText} {FormatTime(snapshot.Timestamp)}");
                    Notify(snapshot, e.Event.Severity);
                    break;

                case ProbeEventKind.Recovered:
                    _logging.Log($"{prefix} {e.Event.Message}");

                    if (_global.NotifyOnRecovery)
                        Notify(snapshot, e.Event.Severity);

                    break;

                case ProbeEventKind.LossAlarm:
                case ProbeEventKind.Flapping:
                    _logging.Log($"{prefix} 告警: {e.Event.Message}");
                    task.Warnings.Enqueue($"{e.Event.Message} {FormatTime(snapshot.Timestamp)}");
                    Notify(snapshot, e.Event.Severity);
                    break;

                case ProbeEventKind.DelaySpike:
                    _logging.Log($"{prefix} {e.Event.Message}");

                    if (_global.EnableVerboseLogging)
                    {
                        _logging.Log(
                            $"{prefix} 统计: 发送 {snapshot.Statistics.Sent} / 接收 {snapshot.Statistics.Received} / " +
                            $"丢包率 {snapshot.LossText} / 故障率 {snapshot.FailureText}");
                    }

                    break;
            }
        }

        private void Notify(ProbeTaskSnapshot snapshot, ProbeSeverity severity)
        {
            if (_notifier is not { IsEnabled: true } notifier)
                return;

            // 冷却键包含协议与端口：同一主机的不同服务不能互相压制。
            string key = $"{snapshot.Name}|{snapshot.Protocol}|{snapshot.Endpoint}";

            if (TryEnterNotificationWindow(key, severity))
                DispatchNotification(notifier, snapshot);
        }

        /// <summary>
        /// 通知冷却。Critical 事件不受冷却限制——中断确认必须送达；
        /// 但仍然刷新时间戳，避免紧随其后的 Warning 重复发送。
        /// </summary>
        private bool TryEnterNotificationWindow(string key, ProbeSeverity severity)
        {
            int cooldown = _global.NotificationCooldownSeconds;
            DateTimeOffset now = DateTimeOffset.Now;

            lock (_notificationGate)
            {
                TrimNotificationWindows(now);

                bool force = severity >= ProbeSeverity.Critical;

                if (!force
                    && cooldown > 0
                    && _lastNotification.TryGetValue(key, out DateTimeOffset last)
                    && (now - last).TotalSeconds < cooldown)
                {
                    return false;
                }

                _lastNotification[key] = now;
                return true;
            }
        }

        /// <summary>
        /// 长期运行时冷却字典只增不减；超过阈值时清掉一天前的记录。
        /// </summary>
        private void TrimNotificationWindows(DateTimeOffset now)
        {
            if (_lastNotification.Count <= NotificationKeyLimit)
                return;

            List<string> expired = [];

            foreach ((string key, DateTimeOffset last) in _lastNotification)
            {
                if (now - last >= NotificationKeyRetention)
                    expired.Add(key);
            }

            foreach (string key in expired)
                _lastNotification.Remove(key);
        }

        private void DispatchNotification(NotificationService notifier, ProbeTaskSnapshot snapshot)
        {
            Task notification = NotifySafelyAsync(notifier, snapshot);

            lock (_notificationGate)
                _pendingNotifications.Add(notification);

            _ = notification.ContinueWith(
                completed =>
                {
                    lock (_notificationGate)
                        _pendingNotifications.Remove(completed);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task NotifySafelyAsync(NotificationService notifier, ProbeTaskSnapshot snapshot)
        {
            try
            {
                await notifier.NotifyStatusChangeAsync(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logging.Log($"通知发送失败: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// 告警队列里只保留时分秒：表格标题已有日期，日志文件自带完整时间戳。
        /// 保留完整日期会让文案变长，在窄窗口里把表格行数撑高。
        /// </summary>
        private static string FormatTime(DateTimeOffset timestamp) => timestamp.ToString("HH:mm:ss");

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            // 先停探测并等待在途通知（最多 10 秒），再释放任务与传输层。
            await StopAsync().ConfigureAwait(false);

            foreach (ProbeTask task in _tasks)
                await task.DisposeAsync().ConfigureAwait(false);

            _tasks.Clear();
        }
    }
}
