using System.Net.Http.Headers;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Pings.Configuration;
using Pings.Diagnostics;
using Pings.Monitoring;

namespace Pings.Notifications
{
    /// <summary>
    /// Webhook / Email 通知发送器。
    /// </summary>
    /// <remarks>
    /// 所有失败都只记录日志，不向调用方抛出：通知是「尽力而为」，
    /// 不能因为某个 Webhook 挂了就中断监控。
    /// </remarks>
    public sealed class NotificationService : IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan SmtpTimeout = TimeSpan.FromSeconds(20);

        private readonly NotificationsConfig _config;
        private readonly Logging _logging;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public NotificationService(Logging logging, NotificationsConfig config)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            SocketsHttpHandler handler = new()
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };

            _httpClient = new HttpClient(handler) { Timeout = RequestTimeout };
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.ProductName, AppInfo.Version));
        }

        /// <summary>Webhook 或 Email 至少启用了一项。</summary>
        public bool IsEnabled => _config.Webhook.Enabled || _config.Email.Enabled;

        public async Task NotifyStatusChangeAsync(ProbeTaskSnapshot snapshot, CancellationToken token = default)
        {
            if (snapshot is null)
                return;

            if (_config.Webhook.Enabled && !string.IsNullOrWhiteSpace(_config.Webhook.Url))
                await SendWebhookAsync(snapshot, token).ConfigureAwait(false);

            if (_config.Email.Enabled && !string.IsNullOrWhiteSpace(_config.Email.SmtpServer))
                await SendEmailAsync(snapshot, token).ConfigureAwait(false);
        }

        private async Task SendWebhookAsync(ProbeTaskSnapshot snapshot, CancellationToken token)
        {
            WebhookConfig webhook = _config.Webhook;

            try
            {
                HttpMethod method = new(string.IsNullOrWhiteSpace(webhook.Method) ? "POST" : webhook.Method.Trim().ToUpperInvariant());
                Dictionary<string, string> variables = BuildVariables(snapshot);
                string body = TemplateRenderer.Render(webhook.Content, variables);

                using HttpRequestMessage request = new(method, webhook.Url);

                if (!string.IsNullOrEmpty(body))
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                if (!string.IsNullOrWhiteSpace(webhook.AuthType) && !string.IsNullOrWhiteSpace(webhook.AuthToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue(webhook.AuthType, webhook.AuthToken);

                if (webhook.Headers is not null)
                {
                    foreach ((string key, string value) in webhook.Headers)
                    {
                        string rendered = TemplateRenderer.Render(value, variables);

                        if (!request.Headers.TryAddWithoutValidation(key, rendered))
                            request.Content?.Headers.TryAddWithoutValidation(key, rendered);
                    }
                }

                using HttpResponseMessage response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    _logging.Log($"已触发Webhook，状态码: {(int)response.StatusCode}");
                else
                    _logging.Log($"Webhook返回非成功状态码: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logging.Log($"Webhook触发失败: {ex.GetType().Name} - {ex.Message}");
            }
        }

        private async Task SendEmailAsync(ProbeTaskSnapshot snapshot, CancellationToken token)
        {
            EmailConfig email = _config.Email;

            try
            {
                MimeMessage message = new();
                message.From.Add(MailboxAddress.Parse(email.From));

                foreach (string address in email.To)
                {
                    if (!string.IsNullOrWhiteSpace(address))
                        message.To.Add(MailboxAddress.Parse(address));
                }

                message.Subject = $"[{AppInfo.ProductName}] {snapshot.Name} ({snapshot.Endpoint}) 状态：{snapshot.StatusText}";

                BodyBuilder body = new()
                {
                    TextBody = BuildPlainTextBody(snapshot),
                    HtmlBody = BuildHtmlBody(snapshot)
                };

                message.Body = body.ToMessageBody();

                using SmtpClient client = new() { Timeout = (int)SmtpTimeout.TotalMilliseconds };

                // 保持与旧版一致的 TLS 语义：enableSsl 为 true 时隐式 TLS（通常 465）。
                SecureSocketOptions options = email.EnableSsl
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.None;

                await client.ConnectAsync(email.SmtpServer, email.Port, options, token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(email.Username))
                    await client.AuthenticateAsync(email.Username, email.Password, token).ConfigureAwait(false);

                await client.SendAsync(message, token).ConfigureAwait(false);
                await client.DisconnectAsync(true, token).ConfigureAwait(false);

                _logging.Log($"已发送通知到: {string.Join(", ", email.To)}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (AuthenticationException ex)
            {
                _logging.Log($"Email认证失败 - SMTP服务器: {email.SmtpServer}, 错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logging.Log($"Email发送失败: {ex.GetType().Name} - {ex.Message}");
            }
        }

        private static Dictionary<string, string> BuildVariables(ProbeTaskSnapshot snapshot) => new(StringComparer.Ordinal)
        {
            ["Name"] = snapshot.Name,
            ["Target"] = snapshot.Target,
            ["Endpoint"] = snapshot.Endpoint,
            ["Protocol"] = snapshot.Protocol.ToConfigString(),
            ["State"] = snapshot.StatusText,
            ["Status"] = snapshot.Status.ToString(),
            ["Category"] = snapshot.Category.ToString(),
            ["Severity"] = snapshot.Severity.ToString(),
            ["Delay"] = snapshot.DelayMs is { } delay ? $"{(int)delay}" : "-1",
            ["Connect"] = snapshot.ConnectMs is { } connect ? $"{(int)connect}" : "-1",
            ["Jitter"] = snapshot.Statistics.JitterMs is { } jitter ? $"{jitter:0.#}" : string.Empty,
            ["LossRate"] = $"{snapshot.Statistics.WindowLossRate * 100:0.0}",
            ["TotalLossRate"] = $"{snapshot.Statistics.TotalLossRate * 100:0.0}",
            ["FailureRate"] = $"{snapshot.Statistics.WindowFailureRate * 100:0.0}",
            ["TotalFailureRate"] = $"{snapshot.Statistics.TotalFailureRate * 100:0.0}",
            ["Sent"] = snapshot.Statistics.Sent.ToString(),
            ["Received"] = snapshot.Statistics.Received.ToString(),
            ["Lost"] = snapshot.Statistics.Lost.ToString(),
            ["Failed"] = snapshot.Statistics.Failed.ToString(),
            ["LastEvent"] = snapshot.LastEvent,
            ["Warning"] = snapshot.Warning ?? string.Empty,
            ["CurrentTime"] = snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
        };

        private static string BuildPlainTextBody(ProbeTaskSnapshot snapshot)
        {
            StringBuilder body = new();
            body.AppendLine($"{AppInfo.ProductName} 告警");
            body.AppendLine();
            body.AppendLine($"任务名称：{snapshot.Name}");
            body.AppendLine($"目标端点：{snapshot.Endpoint}");
            body.AppendLine($"探测协议：{snapshot.Protocol.ToConfigString()}");
            body.AppendLine($"当前状态：{snapshot.StatusText}");
            body.AppendLine($"网络延迟：{snapshot.DelayText}");
            body.AppendLine($"丢包率(窗口 {snapshot.Statistics.WindowCount} 次)：{snapshot.LossText}");
            body.AppendLine($"故障率(窗口 {snapshot.Statistics.WindowCount} 次)：{snapshot.FailureText}");
            body.AppendLine($"累计丢包率：{snapshot.Statistics.TotalLossRate * 100:0.0}%");
            body.AppendLine($"最近事件：{snapshot.LastEvent}");
            body.AppendLine($"发生时间：{snapshot.Timestamp:yyyy-MM-dd HH:mm:ss}");
            return body.ToString();
        }

        private static string BuildHtmlBody(ProbeTaskSnapshot snapshot)
        {
            StringBuilder body = new();
            body.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>");
            body.AppendLine($"<h2>{AppInfo.ProductName} 告警</h2>");
            body.AppendLine("<table cellpadding=\"6\" cellspacing=\"0\" border=\"1\" style=\"border-collapse:collapse\">");
            AppendRow(body, "任务名称", snapshot.Name);
            AppendRow(body, "目标端点", snapshot.Endpoint);
            AppendRow(body, "探测协议", snapshot.Protocol.ToConfigString());
            AppendRow(body, "当前状态", snapshot.StatusText);
            AppendRow(body, "网络延迟", snapshot.DelayText);
            AppendRow(body, "丢包率", snapshot.LossText);
            AppendRow(body, "故障率", snapshot.FailureText);
            AppendRow(body, "累计丢包率", $"{snapshot.Statistics.TotalLossRate * 100:0.0}%");
            AppendRow(body, "最近事件", snapshot.LastEvent);
            AppendRow(body, "发生时间", snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            body.AppendLine("</table>");
            body.AppendLine("</body></html>");
            return body.ToString();
        }

        private static void AppendRow(StringBuilder builder, string key, string value)
            => builder.AppendLine($"<tr><td><b>{System.Net.WebUtility.HtmlEncode(key)}</b></td><td>{System.Net.WebUtility.HtmlEncode(value)}</td></tr>");

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
