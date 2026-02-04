using Spectre.Console;
using System.Net.NetworkInformation;

namespace Pings
{
    /// <summary>
    /// ICMP 监控器类，管理多个 ICMP 测试任务并显示监控界面
    /// </summary>
    class ICMPMonitor
    {
        private CancellationTokenSource CancellationTokenSource { get; set; }
        private Logging? Logging { get; set; }
        private NotificationsConfig Notifications { get; set; }
        private NotificationService Notifier { get; set; }
        /// <summary>任务映射表：IP地址 -> 表格行索引</summary>
        private Dictionary<string, int> TaskMap { get; set; }
        /// <summary>任务显示表格</summary>
        public Table TasksTable { get; set; }
        /// <summary>ICMP 测试任务列表</summary>
        public List<ICMPTestTask> Tasks { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="cancellationTokenSource">取消令牌源</param>
        /// <param name="logging">日志记录器</param>
        public ICMPMonitor(CancellationTokenSource cancellationTokenSource, Logging? logging = null, NotificationsConfig? notifications = null)
        {
            CancellationTokenSource = cancellationTokenSource;
            Logging = logging;
            Notifications = notifications ?? new NotificationsConfig();
            Notifier = new NotificationService(Notifications, Logging);
            Tasks = [];
            TaskMap = [];
            TasksTable = new() { Caption = new TableTitle("确认警告(C) / 退出(Q)") };
            TasksTable.AddColumns("名称", "IP/域名", "状态", "延迟", "警告/日志");
            TasksTable.Centered();
        }

        public void AddHost(ICMPTestTask newTask)
        {
            TaskMap.Add(newTask.IP, TasksTable.Rows.Add([new Text(newTask.Name), new Text(newTask.IP), new Text(newTask.State.ToChineseString()), new Text($"{newTask.Delay.TotalMilliseconds}ms"), new Text(newTask.LastLog)]));

            newTask.LastLogChanged += (task) =>
            {
                if (!task.IsWarning)
                {
                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog));
                }
            };
            newTask.DelayChanged += (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 3, new Text($"{(int)task.Delay.TotalMilliseconds}ms"));
            };
            newTask.OpenWarning += async (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) 触发警告<{task.State.ToChineseString()}>");

                TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(await task.Warnings.DequeueAsync(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));
            };
            newTask.ConfirmWarning += async (task) =>
            {
                if (!task.IsWarning)
                {
                    Logging?.Log($"{task.Name}({task.IP}) 解除警告<{task.State.ToChineseString()}>");

                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog));
                }
                else
                {
                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(await task.Warnings.DequeueAsync(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));
                }
            };
            newTask.StatusChanged += (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) {task.State.ToChineseString()}{(task.State == IPStatus.Success ? $" {(int)task.Delay.TotalMilliseconds}ms" : "")}");

                TasksTable.Rows.Update(TaskMap[task.IP], 2, task.State == IPStatus.Success ? new Text(task.State.ToChineseString()) : new Text(task.State.ToChineseString(), new Style(Color.Yellow, Color.Red, Decoration.Bold)));

                task.LastLog = $"{task.State.ToChineseString()} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ";
                if (task.State != IPStatus.Success)
                {
                    _ = task.Warnings.EnqueueAsync($"{task.State.ToChineseString()} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
                }
                _ = Notifier.NotifyStatusChangeAsync(task);
            };
            newTask.DelayExceptionOccurred += (task) =>
            {
                Logging?.Log($"{task.Name}({task.IP}) 延迟波动<{(int)task.PreviousDelay.TotalMilliseconds}ms → {(int)task.Delay.TotalMilliseconds}ms>");

                task.LastLog = $"延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms → {(int)task.Delay.TotalMilliseconds}ms";
            };

            Tasks.Add(newTask);
        }
    }
}