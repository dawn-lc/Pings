using System.Net.Http.Headers;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Pings.Configuration;
using Pings.Diagnostics;
using Pings.Monitoring;
using Pings.Properties;

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
                Dictionary<string, string> variables = NotificationVariables.Create(snapshot);
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
                    _logging.Log(string.Format(Resources.NotificationWebhookSucceeded, (int)response.StatusCode));
                else
                    _logging.Log(string.Format(Resources.NotificationWebhookNonSuccess, (int)response.StatusCode, response.ReasonPhrase));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logging.Log(string.Format(Resources.NotificationWebhookFailed, ex.GetType().Name, ex.Message));
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

                EmailContent content = EmailContent.Create(email, snapshot);

                message.Subject = content.Subject;

                BodyBuilder body = new()
                {
                    TextBody = content.TextBody,
                    HtmlBody = content.HtmlBody
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

                _logging.Log(string.Format(Resources.NotificationEmailSent, string.Join(", ", email.To)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (AuthenticationException ex)
            {
                _logging.Log(string.Format(Resources.NotificationEmailAuthFailed, email.SmtpServer, ex.Message));
            }
            catch (Exception ex)
            {
                _logging.Log(string.Format(Resources.NotificationEmailFailed, ex.GetType().Name, ex.Message));
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
