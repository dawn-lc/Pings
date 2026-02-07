using Spectre.Console;
using System.Net.NetworkInformation;

namespace Pings
{
    /// <summary>
    /// ICMP 监控器类，管理多个 ICMP 测试任务并显示监控界面
    /// </summary>
    class ICMPMonitor
    {
        private Logging Logging { get; set; }
        private NotificationService? Notifier { get; set; }
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
        public ICMPMonitor(Logging logging, NotificationService? notifier)
        {
            Logging = logging;
            Notifier = notifier;
            Tasks = [];
            TaskMap = [];
            TasksTable = new() { Caption = new TableTitle("确认警告(C) / 退出(Q)") };
            TasksTable.AddColumns("名称", "IP/域名", "状态", "延迟", "警告/日志");
            TasksTable.Centered();
        }

        public void AddHost(ICMPTestTask newTask)
        {
            Style warningColor = new(Color.Yellow, Color.Red, Decoration.Bold);
            TaskMap.Add(
                newTask.IP,
                TasksTable.Rows.Add([
                    new Text(newTask.Name),
                    new Text(newTask.IP),
                    new Text(newTask.State.ToChineseString()),
                    new Text($"{newTask.Delay.TotalMilliseconds}ms"),
                    new Text(newTask.LastLog)
                ])
            );
            newTask.DelayChanged += (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 3, new Text($"{(int)task.Delay.TotalMilliseconds}ms"));
            };
            newTask.OpenWarning += async (task) =>
            {
                Logging.Log($"{task.Name}({task.IP}) 因为 {task.State.ToChineseString()} 触发警告");

                TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(await task.Warnings.PeekAsync(), warningColor));
            };
            newTask.ConfirmWarning += async (task) =>
            {
                TasksTable.Rows.Update(TaskMap[task.IP], 4, task.Warnings.Count < 1 ? new Text(task.LastLog) : new Text(await task.Warnings.PeekAsync(), warningColor));
            };
            newTask.StatusChanged += async (task) =>
            {
                string statusName = task.State.ToChineseString();

                IcmpFaultCategory newCategory = task.State.Classify();
                IcmpFaultCategory previousCategory = task.PreviousState.Classify();

                Logging.Log($"{task.Name}({task.IP}) {statusName}{(task.State == IPStatus.Success ? $" {(int)task.Delay.TotalMilliseconds}ms" : "")}");

                TasksTable.Rows.Update(TaskMap[task.IP], 2, task.State == IPStatus.Success ? new Text(statusName) : new Text(statusName, warningColor));

                task.LastLog = $"{statusName} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]";
                if (newCategory != previousCategory)
                {
                    _ = Notifier?.NotifyStatusChangeAsync(task);

                    if (newCategory != IcmpFaultCategory.None)
                    {
                        await task.Warnings.EnqueueAsync(task.LastLog);
                    }
                }
            };
            newTask.DelayExceptionOccurred += (task) =>
            {
                Logging.Log($"{task.Name}({task.IP}) 延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(int)task.Delay.TotalMilliseconds}ms");

                task.LastLog = $"延迟波动 {(int)task.PreviousDelay.TotalMilliseconds}ms -> {(int)task.Delay.TotalMilliseconds}ms";
            };
            newTask.LastLogChanged += (task) =>
            {
                if (task.Warnings.Count < 1)
                {
                    TasksTable.Rows.Update(TaskMap[task.IP], 4, new Text(task.LastLog));
                }
            };
            Tasks.Add(newTask);
        }
    }
}