using System.Globalization;
using System.Net.Mail;
using Pings.Monitoring;

namespace Pings.Configuration
{
    /// <summary>
    /// 配置校验结果：错误会阻止启动，警告只提示。
    /// </summary>
    public sealed record ConfigValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// 启动期配置校验。所有区间检查与 JSON 反序列化分离，
    /// 让「配置写错了」在启动时就有明确提示，而不是运行中才暴露。
    /// </summary>
    public static class ConfigValidator
    {
        public static ConfigValidationResult Validate(AppConfig? config)
        {
            List<string> errors = [];
            List<string> warnings = [];

            if (config is null)
            {
                errors.Add("配置内容为空");
                return new ConfigValidationResult(errors, warnings);
            }

            ValidateTasks(config.Tasks, errors, warnings);
            ValidateGlobal(config.Global, errors, warnings);
            ValidateLogging(config.Logging, errors, warnings);
            ValidateNotifications(config.Notifications, errors, warnings);

            return new ConfigValidationResult(errors, warnings);
        }

        private static void ValidateTasks(List<IcmpTaskConfigJson>? tasks, List<string> errors, List<string> warnings)
        {
            if (tasks is null || tasks.Count == 0)
            {
                errors.Add("至少需要一个监控任务 (tasks)");
                return;
            }

            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < tasks.Count; i++)
            {
                IcmpTaskConfigJson task = tasks[i];
                string label = string.IsNullOrWhiteSpace(task.Name) ? $"tasks[{i}]" : task.Name;

                if (string.IsNullOrWhiteSpace(task.Name))
                    errors.Add($"{label}: 任务名称不能为空");

                if (string.IsNullOrWhiteSpace(task.IP))
                    errors.Add($"{label}: 目标地址不能为空");
                else if (!IcmpTaskOptions.IsValidTarget(task.IP.Trim()))
                    errors.Add($"{label}: 目标地址 \"{task.IP}\" 不是有效的 IP 或域名");

                if (!names.Add(task.Name.Trim()))
                    warnings.Add($"{label}: 任务名称重复，实时表格将无法区分");

                if (!targets.Add(task.IP.Trim()))
                    warnings.Add($"{label}: 目标地址重复，会重复探测同一主机");

                if (task.Timeout is < 1 or > 60_000)
                    errors.Add($"{label}: timeout 必须在 1~60000 毫秒之间");

                if (task.Interval is < 0 or > 60_000)
                    errors.Add($"{label}: interval 必须在 0~60000 毫秒之间（0 表示与 timeout 相同）");

                if (task.MaxRecentPackets is < 1 or > 100_000)
                    errors.Add($"{label}: maxRecentPackets 必须在 1~100000 之间");

                if (task.SignificantDelayThreshold is < 0 or > 60_000)
                    errors.Add($"{label}: significantDelayThreshold 必须在 0~60000 毫秒之间");

                if (task.PacketLossDuration is < 0 or > 86_400)
                    errors.Add($"{label}: packetLossDuration 必须在 0~86400 秒之间");

                if (task.PacketLossCount is < 0 or > 1_000_000)
                    errors.Add($"{label}: packetLossCount 必须为非负整数");

                if (task.PacketLossDuration == 0 && task.PacketLossCount == 0)
                    warnings.Add($"{label}: 丢包持续时长与丢包次数阈值均为 0，不会触发丢包告警");
            }
        }

        private static void ValidateGlobal(GlobalConfig? global, List<string> errors, List<string> _)
        {
            if (global is null)
            {
                errors.Add("缺少全局配置 (global)");
                return;
            }

            if (global.UiRefreshInterval is < 200 or > 60_000)
                errors.Add("global.uiRefreshInterval 必须在 200~60000 毫秒之间");

            if (global.AutoConfirmWarningInterval is < 0 or > 86_400)
                errors.Add("global.autoConfirmWarningInterval 必须在 0~86400 秒之间");

            if (global.NotificationCooldownSeconds is < 0 or > 86_400)
                errors.Add("global.notificationCooldownSeconds 必须在 0~86400 秒之间");
        }

        private static void ValidateLogging(LoggingConfig? logging, List<string> errors, List<string> _)
        {
            if (logging is null)
            {
                errors.Add("缺少日志配置 (logging)");
                return;
            }

            if (string.IsNullOrWhiteSpace(logging.LogFilePath))
                errors.Add("logging.logFilePath 不能为空");

            if (logging.MaxLogFileSize is < 1 or > 1024)
                errors.Add("logging.maxLogFileSize 必须在 1~1024 MB 之间");

            if (logging.MaxLogFiles is < 1 or > 100)
                errors.Add("logging.maxLogFiles 必须在 1~100 之间");
        }

        private static void ValidateNotifications(NotificationsConfig? notifications, List<string> errors, List<string> _)
        {
            if (notifications is null)
            {
                errors.Add("缺少通知配置 (notifications)");
                return;
            }

            WebhookConfig webhook = notifications.Webhook ?? new WebhookConfig();
            if (webhook.Enabled)
            {
                if (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    errors.Add("notifications.webhook.url 必须是有效的 http/https 地址");
                }

                if (string.IsNullOrWhiteSpace(webhook.Method))
                    errors.Add("notifications.webhook.method 不能为空");

                if (webhook.Headers is not null && webhook.Headers.Keys.Any(string.IsNullOrWhiteSpace))
                    errors.Add("notifications.webhook.headers 存在空键名");

                if (!string.IsNullOrWhiteSpace(webhook.AuthType) && string.IsNullOrWhiteSpace(webhook.AuthToken))
                    errors.Add("notifications.webhook 配置了 authType 但 authToken 为空");
            }

            EmailConfig email = notifications.Email ?? new EmailConfig();
            if (!email.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(email.SmtpServer))
                errors.Add("notifications.email.smtpServer 不能为空");

            if (email.Port is < 1 or > 65_535)
                errors.Add("notifications.email.port 必须在 1~65535 之间");

            if (!IsValidMailbox(email.From))
                errors.Add("notifications.email.from 不是有效的邮箱地址");

            if (email.To is null || email.To.Count == 0)
                errors.Add("notifications.email.to 至少需要一个收件人");
            else if (email.To.Any(address => !IsValidMailbox(address)))
                errors.Add("notifications.email.to 包含无效的邮箱地址");

            if (!string.IsNullOrWhiteSpace(email.Username) && string.IsNullOrWhiteSpace(email.Password))
                errors.Add("notifications.email 配置了 username 但 password 为空");
        }

        private static bool IsValidMailbox(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return false;

            return MailAddress.TryCreate(address, out MailAddress? parsed)
                && !string.IsNullOrWhiteSpace(parsed.Address)
                && parsed.Address.Contains('@');
        }

        /// <summary>把校验结果格式化成可直接输出的多行文本。</summary>
        public static string Describe(ConfigValidationResult result)
        {
            var builder = new System.Text.StringBuilder();
            foreach (string error in result.Errors)
                builder.AppendLine(CultureInfo.InvariantCulture, $"  ✗ {error}");
            foreach (string warning in result.Warnings)
                builder.AppendLine(CultureInfo.InvariantCulture, $"  ! {warning}");
            return builder.ToString().TrimEnd();
        }
    }
}
