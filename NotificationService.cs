using MailKit.Net.Smtp;
using MimeKit;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace Pings
{
    /// <summary>
    /// 负责发送Webhook与Email通知
    /// </summary>
    class NotificationService(Logging logger, NotificationsConfig config) : IDisposable
    {
        private readonly NotificationsConfig config = config;
        private static readonly HttpClient httpClient = new();
        private bool disposed;

        public async Task NotifyStatusChangeAsync(ICMPTestTask task)
        {
            try
            {
                var payload = new WebhookPayload
                {
                    Name = task.Name,
                    IP = task.IP,
                    PreviousState = task.PreviousState.ToString(),
                    State = task.State.ToString(),
                    DelayMs = (int)task.Delay.TotalMilliseconds,
                    Timestamp = DateTime.Now.ToString("o")
                };

                if (config.Webhook.Enabled && !string.IsNullOrWhiteSpace(config.Webhook.Url))
                {
                    await SendWebhookAsync(payload, config.Webhook);
                }

                if (config.Email.Enabled && !string.IsNullOrWhiteSpace(config.Email.SmtpServer) && config.Email.To?.Count > 0)
                {
                    await SendEmailAsync(task, config.Email);
                }
            }
            catch (Exception ex)
            {
                logger?.Log($"通知发送失败: {ex.Message}");
            }
        }

        private async Task SendWebhookAsync(WebhookPayload payload, WebhookConfig webhook)
        {
            try
            {
                var context = new JsonContext();
                var json = JsonSerializer.Serialize(payload, typeof(WebhookPayload), context);
                using var request = new HttpRequestMessage(new HttpMethod(webhook.Method ?? "POST"), webhook.Url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(webhook.AuthType) && !string.IsNullOrWhiteSpace(webhook.AuthToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(webhook.AuthType, webhook.AuthToken);
                }

                if (webhook.Headers != null)
                {
                    foreach (var kv in webhook.Headers)
                    {
                        if (!request.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                        {
                            request.Content?.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        }
                    }
                }

                var resp = await httpClient.SendAsync(request);
                logger?.Log($"已触发Webhook，状态码: {resp.StatusCode}");
            }
            catch (Exception ex)
            {
                logger?.Log($"Webhook触发失败: {ex.Message}");
            }
        }

        private async Task SendEmailAsync(ICMPTestTask task, EmailConfig email)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(email.From));

                foreach (var to in email.To)
                {
                    if (!string.IsNullOrWhiteSpace(to))
                    {
                        message.To.Add(MailboxAddress.Parse(to));
                    }
                }

                message.Subject = $"[Pings] {task.Name} ({task.IP}) 状态：{task.State.ToChineseString()}";

                // 构建 MIME 多部分消息（支持 HTML 和纯文本）
                var bodyBuilder = new BodyBuilder
                {
                    TextBody = BuildPlainTextBody(task),
                    HtmlBody = BuildHtmlBody(task)
                };

                message.Body = bodyBuilder.ToMessageBody();

                // 异步发送邮件
                using var client = new SmtpClient();

                await client.ConnectAsync(
                    email.SmtpServer,
                    email.Port,
                    email.EnableSsl ? MailKit.Security.SecureSocketOptions.SslOnConnect
                                    : MailKit.Security.SecureSocketOptions.None
                );

                if (!string.IsNullOrWhiteSpace(email.Username))
                {
                    await client.AuthenticateAsync(email.Username, email.Password);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                logger?.Log($"已发送通知到: {string.Join(", ", email.To)}");
            }
            catch (MailKit.Security.AuthenticationException ex)
            {
                logger?.Log($"Email认证失败 - SMTP服务器: {email.SmtpServer}, 错误: {ex.Message}");
            }
            catch (MailKit.ServiceNotConnectedException ex)
            {
                logger?.Log($"Email连接失败 - {email.SmtpServer}:{email.Port}, 错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger?.Log($"Email发送失败: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// 构建纯文本格式的邮件正文
        /// </summary>
        private static string BuildPlainTextBody(ICMPTestTask task)
        {
            var body = new StringBuilder();
            body.AppendLine("Pings 告警");
            body.AppendLine();
            body.AppendLine($"任务名称：{task.Name}");
            body.AppendLine($"目标地址：{task.IP}");
            body.AppendLine($"上次状态：{task.PreviousState.ToChineseString()}");
            body.AppendLine($"当前状态：{task.State.ToChineseString()}");
            body.AppendLine($"网络延迟：{(int)task.Delay.TotalMilliseconds}ms");

            body.AppendLine($"发生时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            return body.ToString();
        }

        /// <summary>
        /// 构建 HTML 格式的邮件正文（更美观）
        /// </summary>
        private static string BuildHtmlBody(ICMPTestTask task)
        {
            var statusColor = task.State == IPStatus.Success ? "#27ae60" : "#e74c3c";
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif; background: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 20px auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ border-bottom: 3px solid {statusColor}; padding-bottom: 15px; margin-bottom: 20px; }}
        .title {{ font-size: 24px; font-weight: bold; color: #333; }}
        .status {{ display: inline-block; background: {statusColor}; color: white; padding: 5px 10px; border-radius: 4px; margin-left: 10px; }}
        .info-row {{ display: flex; padding: 10px 0; border-bottom: 1px solid #eee; }}
        .label {{ flex: 0 0 120px; font-weight: bold; color: #555; }}
        .value {{ flex: 1; color: #333; }}
        .value.warning {{ color: {statusColor}; font-weight: bold; }}
        .footer {{ margin-top: 20px; text-align: center; color: #999; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='title'>Pings 告警 <span class='status'>{task.State.ToChineseString()}</span></div>
        </div>
        <div class='info-row'>
            <div class='label'>任务名称：</div>
            <div class='value'>{System.Web.HttpUtility.HtmlEncode(task.Name)}</div>
        </div>
        <div class='info-row'>
            <div class='label'>目标地址：</div>
            <div class='value'>{System.Web.HttpUtility.HtmlEncode(task.IP)}</div>
        </div>
        <div class='info-row'>
            <div class='label'>上次状态：</div>
            <div class='value'>{task.PreviousState.ToChineseString()}</div>
        </div>
        <div class='info-row'>
            <div class='label'>当前状态：</div>
            <div class='value warning'>{task.State.ToChineseString()}</div>
        </div>
        <div class='info-row'>
            <div class='label'>网络延迟：</div>
            <div class='value'>{(int)task.Delay.TotalMilliseconds}ms</div>
        </div>
        <div class='info-row'>
            <div class='label'>发生时间：</div>
            <div class='value'>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>
        </div>
        <div class='footer'>
            <p>这是一条自动告警邮件，请勿回复。</p>
            <p>Pings</p>
        </div>
    </div>
</body>
</html>";
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源实现
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // 清理静态HttpClient资源
                    httpClient?.Dispose();
                }
                disposed = true;
            }
        }
    }
}
