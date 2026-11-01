using Pings.Configuration;
using Pings.Diagnostics;
using Pings.Monitoring;
using Pings.Notifications;

namespace Pings.Monitoring
{
    /// <summary>
    /// 监控编排器：持有所有任务，把任务事件落到「日志 / 告警队列 / 通知」，
    /// 不参与任何界面渲染（渲染由 <see cref="Pings.Ui.MonitorView"/> 在刷新时读取快照）。
    /// </summary>
    public sealed class IcmpMonitor : IAsyncDisposable
    {
        private readonly Logging _logging;
        private readonly NotificationService? _notifier;
        private readonly GlobalConfig _global;
        private readonly List<IcmpTestTask> _tasks = [];
        private readonly Dictionary<string, DateTimeOffset> _lastNotification = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _notificationGate = new();
        private readonly List<Task> _pendingNotifications = [];
        private bool _disposed;

        public IcmpMonitor(Logging logging, NotificationService? notifier, GlobalConfig? global = null)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _notifier = notifier;
            _global = global ?? new GlobalConfig();
        }

        public IReadOnlyList<IcmpTestTask> Tasks => _tasks;

        public IcmpTestTask Add(IcmpTaskOptions options)
        {
            IcmpTestTask task = new(options);

            task.StatusChanged += OnStatusChanged;
            task.DelaySpiked += OnDelaySpiked;
            task.LossAlarmRaised += OnLossAlarmRaised;

            _tasks.Add(task);
            return task;
        }

        public void StartAll(CancellationToken token = default)
        {
            foreach (IcmpTestTask task in _tasks)
                task.Start(token);
        }

        /// <summary>每个任务执行一次探测（--once 模式）。</summary>
        public async Task ProbeAllOnceAsync(CancellationToken token = default)
        {
            await Task.WhenAll(_tasks.Select(task => task.ProbeOnceAsync(token))).ConfigureAwait(false);
        }

        /// <summary>采集所有任务的只读快照，供界面一次性渲染。</summary>
        public IReadOnlyList<IcmpTaskSnapshot> Snapshots()
            => _tasks.Select(task => task.Snapshot()).ToArray();

        /// <summary>确认（消费）每个任务的队头警告，对应快捷键 C。</summary>
        public void ConfirmAllWarnings()
        {
            foreach (IcmpTestTask task in _tasks)
            {
                if (task.Warnings.Count > 0)
                {
                    string? warning = task.Warnings.Dequeue();
                    if (warning is not null)
                        _logging.Log($"{task.Options.Name}({task.Options.Target}) 已确认警告: {warning}");
                }
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

        private void OnStatusChanged(object? sender, IcmpTaskEventArgs e)
        {
            if (sender is not IcmpTestTask task)
                return;

            IcmpTaskSnapshot snapshot = e.Snapshot;
            string detail = snapshot.IsSuccess
                ? $"{snapshot.StatusText} {snapshot.DelayText}"
                : snapshot.LastEvent;

            _logging.Log($"{snapshot.Name}({snapshot.Target}) {detail}");

            if (snapshot.Category == e.PreviousCategory)
                return;

            if (snapshot.Category != IcmpFaultCategory.None)
                task.Warnings.Enqueue($"{snapshot.StatusText} {FormatTime(snapshot.Timestamp)}");

            bool isRecovery = snapshot.Category == IcmpFaultCategory.None;
            if (isRecovery && !_global.NotifyOnRecovery)
                return;

            if (_notifier is { IsEnabled: true } notifier && TryEnterNotificationWindow(snapshot.Target))
                DispatchNotification(notifier, snapshot);
        }

        private void OnDelaySpiked(object? sender, IcmpTaskEventArgs e)
        {
            IcmpTaskSnapshot snapshot = e.Snapshot;
            _logging.Log($"{snapshot.Name}({snapshot.Target}) {snapshot.LastEvent}");

            if (_global.EnableVerboseLogging)
                _logging.Log($"{snapshot.Name}({snapshot.Target}) 统计: 发送 {snapshot.Sent} / 接收 {snapshot.Received} / 丢包率 {snapshot.LossText}");
        }

        private void OnLossAlarmRaised(object? sender, IcmpTaskEventArgs e)
        {
            if (sender is not IcmpTestTask task)
                return;

            IcmpTaskSnapshot snapshot = e.Snapshot;
            string warning = $"{snapshot.LastEvent} {FormatTime(snapshot.Timestamp)}";

            _logging.Log($"{snapshot.Name}({snapshot.Target}) 丢包告警: {warning}");
            task.Warnings.Enqueue(warning);

            if (_notifier is { IsEnabled: true } notifier && TryEnterNotificationWindow(snapshot.Target))
                DispatchNotification(notifier, snapshot);
        }

        /// <summary>
        /// 告警队列里只保留时分秒：表格标题已有日期，日志文件自带完整时间戳。
        /// 保留完整日期会让文案变长，在窄窗口里把表格行数撑高。
        /// </summary>
        private static string FormatTime(DateTimeOffset timestamp) => timestamp.ToString("HH:mm:ss");

        private bool TryEnterNotificationWindow(string key)
        {
            int cooldown = _global.NotificationCooldownSeconds;
            if (cooldown <= 0)
                return true;

            DateTimeOffset now = DateTimeOffset.Now;

            lock (_notificationGate)
            {
                if (_lastNotification.TryGetValue(key, out DateTimeOffset last)
                    && (now - last).TotalSeconds < cooldown)
                {
                    return false;
                }

                _lastNotification[key] = now;
                return true;
            }
        }

        private void DispatchNotification(NotificationService notifier, IcmpTaskSnapshot snapshot)
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

        private async Task NotifySafelyAsync(NotificationService notifier, IcmpTaskSnapshot snapshot)
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

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            await StopAsync().ConfigureAwait(false);
        }
    }
}
