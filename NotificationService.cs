using MailKit.Net.Smtp;
using MimeKit;
using System.Net.NetworkInformation;
using System.Text;

namespace Pings
{
    /// <summary>
    /// 负责发送Webhook与Email通知
    /// </summary>
    class NotificationService : IDisposable
    {
        private readonly NotificationsConfig Config;
        private readonly HttpClient HttpClient;
        private readonly Logging Logger;
        private bool disposed;
        public NotificationService(Logging logger, NotificationsConfig config)
        {
            Config = config;
            Logger = logger;
            HttpClient = new();
            HttpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Pings", Program.GetAppVersion()));
            HttpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("(https://dawnlc.me)"));
        }

        public async Task NotifyStatusChangeAsync(ICMPTestTask task)
        {
            try
            {
                if (Config.Webhook.Enabled && !string.IsNullOrWhiteSpace(Config.Webhook.Url))
                {
                    await SendWebhookAsync(task, Config.Webhook);
                }

                if (Config.Email.Enabled && !string.IsNullOrWhiteSpace(Config.Email.SmtpServer) && Config.Email.To?.Count > 0)
                {
                    await SendEmailAsync(task, Config.Email);
                }
            }
            catch (Exception ex)
            {
                Logger?.Log($"通知发送失败: {ex.Message}");
            }
        }

        private async Task SendWebhookAsync(ICMPTestTask task, WebhookConfig webhook)
        {
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(webhook.Method ?? "POST"), webhook.Url)
                {
                    Content = new StringContent(ApplyTemplate(webhook.Content, task), Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrWhiteSpace(webhook.AuthType) && !string.IsNullOrWhiteSpace(webhook.AuthToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(webhook.AuthType, webhook.AuthToken);
                }
                if (webhook.Headers != null)
                {
                    foreach (var kv in webhook.Headers)
                    {
                        var headerValue = ApplyTemplate(kv.Value, task);
                        if (!request.Headers.TryAddWithoutValidation(kv.Key, headerValue))
                        {
                            request.Content?.Headers.TryAddWithoutValidation(kv.Key, headerValue);
                        }
                    }
                }
                var resp = await HttpClient.SendAsync(request);
                Logger?.Log($"已触发Webhook，状态码: {resp.StatusCode}");
            }
            catch (Exception ex)
            {
                Logger?.Log($"Webhook触发失败: {ex}");
            }
        }

        /// <summary>
        /// 应用模板替换，将占位符替换为实际值
        /// </summary>
        private static string ApplyTemplate(string template, ICMPTestTask task)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            // 创建变量字典
            var variables = new Dictionary<string, string>
            {
                ["Name"] = task.Name,
                ["IP"] = task.IP,
                ["PreviousState"] = task.PreviousState.ToChineseString(),
                ["State"] = task.State.ToChineseString(),
                ["Delay"] = ((int)task.Delay.TotalMilliseconds).ToString(),
                ["LastLog"] = task.LastLog,
                ["PreviousDelay"] = ((int)task.PreviousDelay.TotalMilliseconds).ToString(),
                ["CurrentTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            // 使用新的模板替换方法
            return ReplaceVariables(template, variables, "%#", "#%");
        }

        /// <summary>
        /// 替换字符串中的变量占位符（使用 C# 内置方法）
        /// </summary>
        /// <param name="template">模板字符串</param>
        /// <param name="replacements">替换键值对</param>
        /// <param name="prefix">占位符前缀，默认 "%#"</param>
        /// <param name="suffix">占位符后缀，默认 "#%"</param>
        /// <returns>替换后的字符串</returns>
        private static string ReplaceVariables(string template, Dictionary<string, string> replacements, string prefix = "%#", string suffix = "#%")
        {
            if (string.IsNullOrEmpty(template) || replacements == null || replacements.Count == 0)
                return template;

            var seenStates = new HashSet<string>();
            var current = template;

            // 防止循环替换
            while (true)
            {
                if (seenStates.Contains(current))
                {
                    break;
                }
                seenStates.Add(current);

                var result = new StringBuilder();
                int position = 0;
                int templateLength = current.Length;
                int prefixLength = prefix.Length;
                int suffixLength = suffix.Length;
                bool changed = false;

                while (position < templateLength)
                {
                    // 使用 IndexOf 查找前缀
                    int prefixIndex = current.IndexOf(prefix, position, StringComparison.Ordinal);

                    if (prefixIndex == -1)
                    {
                        // 没有更多占位符，添加剩余部分
                        result.Append(current[position..]);
                        break;
                    }

                    // 添加前缀之前的部分
                    result.Append(current[position..prefixIndex]);

                    // 查找后缀
                    int suffixIndex = current.IndexOf(suffix, prefixIndex + prefixLength, StringComparison.Ordinal);

                    if (suffixIndex == -1)
                    {
                        // 没有找到匹配的后缀，添加剩余部分并退出
                        result.Append(current[prefixIndex..]);
                        break;
                    }

                    // 提取占位符内容
                    int placeholderStart = prefixIndex + prefixLength;
                    int placeholderLength = suffixIndex - placeholderStart;
                    string placeholderContent = current.Substring(placeholderStart, placeholderLength);

                    // 检查是否有格式化部分
                    int colonIndex = placeholderContent.IndexOf(':');
                    string? key, format = null;

                    if (colonIndex > 0)
                    {
                        key = placeholderContent[..colonIndex];
                        format = placeholderContent[(colonIndex + 1)..];
                    }
                    else
                    {
                        key = placeholderContent;
                    }

                    // 查找替换值
                    if (replacements.TryGetValue(key, out var replacement))
                    {
                        // 应用格式化
                        if (!string.IsNullOrEmpty(format))
                        {
                            // 简单的格式化支持
                            if (DateTime.TryParse(replacement, out var dateValue))
                            {
                                try
                                {
                                    replacement = dateValue.ToString(format);
                                }
                                catch
                                {
                                }
                            }
                            // 可以添加其他类型的格式化支持
                        }

                        result.Append(replacement);
                        changed = true;
                    }
                    else
                    {
                        // 没有找到替换值，保留原始占位符
                        result.Append(prefix);
                        result.Append(placeholderContent);
                        result.Append(suffix);
                    }

                    // 移动到后缀之后
                    position = suffixIndex + suffixLength;
                }

                if (!changed)
                {
                    // 没有更多替换，退出循环
                    break;
                }

                current = result.ToString();
            }

            return current;
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

                Logger?.Log($"已发送通知到: {string.Join(", ", email.To)}");
            }
            catch (MailKit.Security.AuthenticationException ex)
            {
                Logger?.Log($"Email认证失败 - SMTP服务器: {email.SmtpServer}, 错误: {ex.Message}");
            }
            catch (MailKit.ServiceNotConnectedException ex)
            {
                Logger?.Log($"Email连接失败 - {email.SmtpServer}:{email.Port}, 错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger?.Log($"Email发送失败: {ex.GetType().Name} - {ex.Message}");
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
                    HttpClient?.Dispose();
                }
                disposed = true;
            }
        }
    }
}
