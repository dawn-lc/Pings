using System.Globalization;
using System.Net.Mail;
using Pings.Localization;
using Pings.Monitoring;
using Pings.Properties;

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
                errors.Add(Resources.ConfigContentEmpty);
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
                errors.Add(Resources.ValidationAtLeastOneTask);
                return;
            }

            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> endpoints = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < tasks.Count; i++)
            {
                ProbeTaskConfigJson task = tasks[i];
                string label = string.IsNullOrWhiteSpace(task.Name) ? $"tasks[{i}]" : task.Name;

                if (string.IsNullOrWhiteSpace(task.Name))
                    errors.Add(string.Format(Resources.ValidationTaskNameRequired, label));
                else if (!names.Add(task.Name.Trim()))
                    warnings.Add(string.Format(Resources.ValidationTaskNameDuplicate, label));

                bool protocolValid = ProbeProtocolExtensions.TryParseProtocol(task.Protocol, out ProbeProtocol protocol);

                if (!protocolValid)
                    errors.Add(string.Format(Resources.ValidationProtocolInvalid, label, task.Protocol));

                bool targetValid = ValidateTarget(task, protocolValid ? protocol : ProbeProtocol.Icmp, label, errors);

                if (protocolValid)
                    ValidateProtocolFields(task, protocol, baseDirectory, label, errors, warnings);

                if (protocolValid && targetValid
                    && !endpoints.Add(ProbeTaskOptions.BuildEndpoint(protocol, task.Target.Trim(), task.Port ?? 0)))
                {
                    warnings.Add(string.Format(Resources.ValidationEndpointDuplicate, label));
                }

                if (task.Timeout is < 1 or > 60_000)
                    errors.Add(string.Format(Resources.ValidationTimeoutOutOfRange, label));

                if (task.Interval is < 0 or > 60_000)
                    errors.Add(string.Format(Resources.ValidationIntervalOutOfRange, label));
                else if (task.Interval > 0 && task.Interval < task.Timeout)
                    warnings.Add(string.Format(Resources.ValidationIntervalLessThanTimeout, label));

                if (task.MaxRecentPackets is < 1 or > 100_000)
                    errors.Add(string.Format(Resources.ValidationMaxRecentPacketsOutOfRange, label));

                if (task.SignificantDelayThreshold is < 0 or > 60_000)
                    errors.Add(string.Format(Resources.ValidationSignificantDelayThresholdOutOfRange, label));

                if (task.PacketLossDuration is < 0 or > 86_400)
                    errors.Add(string.Format(Resources.ValidationPacketLossDurationOutOfRange, label));

                if (task.PacketLossCount is < 0 or > 1_000_000)
                    errors.Add(string.Format(Resources.ValidationPacketLossCountInvalid, label));

                if (task.LossFlapWindowSeconds is < 0 or > 86_400)
                    errors.Add(string.Format(Resources.ValidationLossFlapWindowOutOfRange, label));

                if (task.LossFlapCount is < 0 or > 1_000_000)
                    errors.Add(string.Format(Resources.ValidationLossFlapCountInvalid, label));

                if (task.PacketLossDuration == 0 && task.PacketLossCount == 0)
                    warnings.Add(string.Format(Resources.ValidationLossThresholdsBothZero, label));
            }
        }

        private static bool ValidateTarget(ProbeTaskConfigJson task, ProbeProtocol protocol, string label, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(task.Target))
            {
                errors.Add(string.Format(Resources.ValidationTargetRequired, label));
                return false;
            }

            string target = task.Target.Trim();

            if (protocol == ProbeProtocol.Http)
            {
                if (!ProbeTaskOptions.IsValidHttpUrl(target))
                {
                    errors.Add(string.Format(Resources.ValidationTargetNotHttpUrl, label, target));
                    return false;
                }

                return true;
            }

            if (!ProbeTaskOptions.IsValidHost(target))
            {
                errors.Add(string.Format(Resources.ValidationTargetNotHost, label, target));
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
                    errors.Add(string.Format(Resources.ValidationPortRequired, label, protocol.ToConfigString()));
            }
            else if (task.Port is not null)
            {
                warnings.Add(string.Format(Resources.ValidationPortIgnored, label, protocol.ToConfigString()));
            }

            switch (protocol)
            {
                case ProbeProtocol.Icmp:
                    if (task.PayloadSize is < 0 or > 65_500)
                        errors.Add(string.Format(Resources.ValidationPayloadSizeOutOfRange, label));
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
                            errors.Add(string.Format(Resources.ValidationHttpMethodNotAllowed, label));
                    }

                    if (task.ExpectedStatus is not null)
                    {
                        foreach (int code in task.ExpectedStatus)
                        {
                            if (code is < 100 or > 599)
                                errors.Add(string.Format(Resources.ValidationExpectedStatusOutOfRange, label, code));
                        }
                    }
                    break;
            }
        }

        private static void ValidateGlobal(GlobalConfig? global, List<string> errors, List<string> _)
        {
            if (global is null)
            {
                errors.Add(Resources.ValidationMissingGlobalSection);
                return;
            }

            if (global.UiRefreshInterval is < 200 or > 60_000)
                errors.Add(Resources.ValidationUiRefreshIntervalOutOfRange);

            if (global.AutoConfirmWarningInterval is < 0 or > 86_400)
                errors.Add(Resources.ValidationAutoConfirmIntervalOutOfRange);

            if (global.NotificationCooldownSeconds is < 0 or > 86_400)
                errors.Add(Resources.ValidationNotificationCooldownOutOfRange);

            if (!UiLanguage.TryResolve(global.Language, out CultureInfo? culture))
                errors.Add(string.Format(Resources.ValidationLanguageInvalid, global.Language, UiLanguage.SupportedMarkerList));
        }

        private static void ValidateLogging(LoggingConfig? logging, List<string> errors, List<string> _)
        {
            if (logging is null)
            {
                errors.Add(Resources.ValidationMissingLoggingSection);
                return;
            }

            if (string.IsNullOrWhiteSpace(logging.LogFilePath))
                errors.Add(Resources.ValidationLogFilePathRequired);

            if (logging.MaxLogFileSize is < 1 or > 1024)
                errors.Add(Resources.ValidationMaxLogFileSizeOutOfRange);

            if (logging.MaxLogFiles is < 1 or > 100)
                errors.Add(Resources.ValidationMaxLogFilesOutOfRange);
        }

        private static void ValidateNotifications(NotificationsConfig? notifications, List<string> errors, List<string> _)
        {
            if (notifications is null)
            {
                errors.Add(Resources.ValidationMissingNotificationsSection);
                return;
            }

            WebhookConfig webhook = notifications.Webhook ?? new WebhookConfig();
            if (webhook.Enabled)
            {
                if (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    errors.Add(Resources.ValidationWebhookUrlInvalid);
                }

                if (string.IsNullOrWhiteSpace(webhook.Method))
                    errors.Add(Resources.ValidationWebhookMethodRequired);

                if (webhook.Headers is not null && webhook.Headers.Keys.Any(string.IsNullOrWhiteSpace))
                    errors.Add(Resources.ValidationWebhookEmptyHeaderKey);

                if (!string.IsNullOrWhiteSpace(webhook.AuthType) && string.IsNullOrWhiteSpace(webhook.AuthToken))
                    errors.Add(Resources.ValidationWebhookAuthTokenRequired);
            }

            EmailConfig email = notifications.Email ?? new EmailConfig();
            if (!email.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(email.SmtpServer))
                errors.Add(Resources.ValidationEmailSmtpServerRequired);

            if (email.Port is < 1 or > 65_535)
                errors.Add(Resources.ValidationEmailPortOutOfRange);

            if (!IsValidMailbox(email.From))
                errors.Add(Resources.ValidationEmailFromInvalid);

            if (email.To is null || email.To.Count == 0)
                errors.Add(Resources.ValidationEmailToRequired);
            else if (email.To.Any(address => !IsValidMailbox(address)))
                errors.Add(Resources.ValidationEmailToInvalid);

            if (!string.IsNullOrWhiteSpace(email.Username) && string.IsNullOrWhiteSpace(email.Password))
                errors.Add(Resources.ValidationEmailPasswordRequired);
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
