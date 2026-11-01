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
        public static ConfigValidationResult Validate(AppConfig? config, string? baseDirectory = null)
        {
            List<string> errors = [];
            List<string> warnings = [];

            if (config is null)
            {
                errors.Add("配置内容为空");
                return new ConfigValidationResult(errors, warnings);
            }

            ValidateTasks(config.Tasks, baseDirectory, errors, warnings);
            ValidateGlobal(config.Global, errors, warnings);
            ValidateLogging(config.Logging, errors, warnings);
            ValidateNotifications(config.Notifications, errors, warnings);

            return new ConfigValidationResult(errors, warnings);
        }

        private static void ValidateTasks(
            List<ProbeTaskConfigJson>? tasks,
            string? baseDirectory,
            List<string> errors,
            List<string> warnings)
        {
            if (tasks is null || tasks.Count == 0)
            {
                errors.Add("至少需要一个监控任务 (tasks)");
                return;
            }

            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> endpoints = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < tasks.Count; i++)
            {
                ProbeTaskConfigJson task = tasks[i];
                string label = string.IsNullOrWhiteSpace(task.Name) ? $"tasks[{i}]" : task.Name;

                if (string.IsNullOrWhiteSpace(task.Name))
                    errors.Add($"{label}: 任务名称不能为空");
                else if (!names.Add(task.Name.Trim()))
                    warnings.Add($"{label}: 任务名称重复，实时表格将无法区分");

                bool protocolValid = ProbeProtocolExtensions.TryParseProtocol(task.Protocol, out ProbeProtocol protocol);

                if (!protocolValid)
                    errors.Add($"{label}: protocol \"{task.Protocol}\" 无效，可选 icmp / tcp / udp / http");

                bool targetValid = ValidateTarget(task, protocolValid ? protocol : ProbeProtocol.Icmp, label, errors);

                if (protocolValid)
                    ValidateProtocolFields(task, protocol, baseDirectory, label, errors, warnings);

                if (protocolValid && targetValid
                    && !endpoints.Add(ProbeTaskOptions.BuildEndpoint(protocol, task.Target.Trim(), task.Port ?? 0)))
                {
                    warnings.Add($"{label}: 目标重复，会重复探测同一端点");
                }

                if (task.Timeout is < 1 or > 60_000)
                    errors.Add($"{label}: timeout 必须在 1~60000 毫秒之间");

                if (task.Interval is < 0 or > 60_000)
                    errors.Add($"{label}: interval 必须在 0~60000 毫秒之间（0 表示与 timeout 相同）");
                else if (task.Interval > 0 && task.Interval < task.Timeout)
                    warnings.Add($"{label}: interval 小于 timeout，实际探测间隔会被探测耗时撑长");

                if (task.MaxRecentPackets is < 1 or > 100_000)
                    errors.Add($"{label}: maxRecentPackets 必须在 1~100000 之间");

                if (task.SignificantDelayThreshold is < 0 or > 60_000)
                    errors.Add($"{label}: significantDelayThreshold 必须在 0~60000 毫秒之间");

                if (task.PacketLossDuration is < 0 or > 86_400)
                    errors.Add($"{label}: packetLossDuration 必须在 0~86400 秒之间");

                if (task.PacketLossCount is < 0 or > 1_000_000)
                    errors.Add($"{label}: packetLossCount 必须为非负整数");

                if (task.LossFlapWindowSeconds is < 0 or > 86_400)
                    errors.Add($"{label}: lossFlapWindowSeconds 必须在 0~86400 秒之间");

                if (task.LossFlapCount is < 0 or > 1_000_000)
                    errors.Add($"{label}: lossFlapCount 必须为非负整数");

                if (task.PacketLossDuration == 0 && task.PacketLossCount == 0)
                    warnings.Add($"{label}: 丢包持续时长与丢包次数阈值均为 0，不会触发中断告警");
            }
        }

        private static bool ValidateTarget(ProbeTaskConfigJson task, ProbeProtocol protocol, string label, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(task.Target))
            {
                errors.Add($"{label}: 目标地址不能为空");
                return false;
            }

            string target = task.Target.Trim();

            if (protocol == ProbeProtocol.Http)
            {
                if (!ProbeTaskOptions.IsValidHttpUrl(target))
                {
                    errors.Add($"{label}: target \"{target}\" 不是有效的 http/https 地址");
                    return false;
                }

                return true;
            }

            if (!ProbeTaskOptions.IsValidHost(target))
            {
                errors.Add($"{label}: 目标地址 \"{target}\" 不是有效的 IP 或域名");
                return false;
            }

            return true;
        }

        private static void ValidateProtocolFields(
            ProbeTaskConfigJson task,
            ProbeProtocol protocol,
            string? baseDirectory,
            string label,
            List<string> errors,
            List<string> warnings)
        {
            if (protocol.RequiresPort())
            {
                if (task.Port is not (>= 1 and <= 65_535))
                    errors.Add($"{label}: {protocol.ToConfigString()} 需要 1~65535 的端口 (port)");
            }
            else if (task.Port is not null)
            {
                warnings.Add($"{label}: {protocol.ToConfigString()} 不使用 port，该字段被忽略");
            }

            switch (protocol)
            {
                case ProbeProtocol.Icmp:
                    if (task.PayloadSize is < 0 or > 65_500)
                        errors.Add($"{label}: payloadSize 必须在 0~65500 之间");
                    break;

                case ProbeProtocol.Udp:
                    // 三种来源（文本 / base64: / file:）共用同一套解析逻辑：
                    // 校验与运行期不可能出现「启动说可以、跑起来却不行」的偏差。
                    if (!UdpPayloadResolver.TryValidate(task.UdpPayload, baseDirectory, out string? payloadError))
                        errors.Add($"{label}: {payloadError}");
                    break;

                case ProbeProtocol.Http:
                    if (task.HttpMethod is not null)
                    {
                        string method = task.HttpMethod.Trim().ToUpperInvariant();
                        if (method is not ("HEAD" or "GET"))
                            errors.Add($"{label}: httpMethod 只允许 HEAD 或 GET");
                    }

                    if (task.ExpectedStatus is not null)
                    {
                        foreach (int code in task.ExpectedStatus)
                        {
                            if (code is < 100 or > 599)
                                errors.Add($"{label}: expectedStatus 包含非法状态码 {code}（100~599）");
                        }
                    }
                    break;
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
