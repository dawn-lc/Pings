using System.Globalization;
using System.Net;
using System.Text;
using Pings.Configuration;
using Pings.Diagnostics;
using Pings.Monitoring;
using Pings.Properties;

namespace Pings.Notifications
{
    /// <summary>
    /// 一封告警邮件的内容：主题 + 纯文本正文 + HTML 正文。
    /// </summary>
    /// <remarks>
    /// 邮件客户端会自行挑选纯文本或 HTML 版本，所以两者必须表达同一件事。
    /// 生成过程不接触 SMTP，便于单元测试直接断言渲染结果。
    /// </remarks>
    public sealed record EmailContent(string Subject, string TextBody, string HtmlBody)
    {
        private const string FontFamily = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

        /// <summary>
        /// 按配置生成邮件内容。三个模板都留空时使用内置模板（随界面语言）。
        /// </summary>
        public static EmailContent Create(EmailConfig config, ProbeTaskSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(snapshot);

            Dictionary<string, string> variables = NotificationVariables.Create(snapshot);

            string subject = string.IsNullOrWhiteSpace(config.Subject)
                ? string.Format(CultureInfo.CurrentCulture, Resources.NotificationEmailSubject, snapshot.Name, snapshot.Endpoint, snapshot.StatusText)
                : TemplateRenderer.Render(config.Subject, variables);

            bool hasCustomText = !string.IsNullOrWhiteSpace(config.Content);
            string textBody = hasCustomText
                ? TemplateRenderer.Render(config.Content, variables)
                : BuildDefaultTextBody(snapshot);

            string htmlBody;

            if (!string.IsNullOrWhiteSpace(config.HtmlContent))
                htmlBody = TemplateRenderer.Render(config.HtmlContent, variables);
            else if (hasCustomText)
                htmlBody = WrapPlainText(textBody);
            else
                htmlBody = BuildDefaultHtmlBody(snapshot);

            return new EmailContent(subject, textBody, htmlBody);
        }

        /// <summary>
        /// 内置正文的字段序列。纯文本与 HTML 两种格式都用它渲染，
        /// 于是新增字段只改一处；早先两份各自硬编码，出现过 HTML 里「丢包率 | 丢包率(窗口…)：…」这种重复标签。
        /// </summary>
        private static (string Label, string Value)[] DefaultFields(ProbeTaskSnapshot snapshot) =>
        [
            (Resources.NotificationLabelTaskName, snapshot.Name),
            (Resources.NotificationLabelEndpoint, snapshot.Endpoint),
            (Resources.NotificationLabelProtocol, snapshot.Protocol.ToConfigString()),
            (Resources.NotificationLabelStatus, snapshot.StatusText),
            (Resources.NotificationLabelDelay, snapshot.DelayText),
            (string.Format(CultureInfo.CurrentCulture, Resources.NotificationLabelLossRateWindow, snapshot.Statistics.WindowCount), snapshot.LossText),
            (string.Format(CultureInfo.CurrentCulture, Resources.NotificationLabelFailureRateWindow, snapshot.Statistics.WindowCount), snapshot.FailureText),
            (Resources.NotificationLabelTotalLossRate, FormatRate(snapshot.Statistics.TotalLossRate)),
            (Resources.NotificationLabelLastEvent, snapshot.LastEvent),
            (Resources.NotificationLabelTimestamp, snapshot.TimestampText)
        ];

        /// <summary>内置纯文本正文。</summary>
        private static string BuildDefaultTextBody(ProbeTaskSnapshot snapshot)
        {
            StringBuilder body = new();
            body.AppendLine($"{AppInfo.ProductName} {Resources.NotificationAlertTitle}");
            body.AppendLine();

            foreach ((string label, string value) in DefaultFields(snapshot))
                AppendTextLine(body, label, value);

            body.AppendLine();
            body.AppendLine(AppInfo.VersionText);

            return body.ToString();
        }

        /// <summary>
        /// 内置 HTML 正文：单列卡片，顶部色条按严重程度着色。
        /// </summary>
        /// <remarks>
        /// 只用 table + 内联样式：邮件客户端会剥掉 <c>&lt;style&gt;</c>，也不保证支持 flex / grid。
        /// </remarks>
        private static string BuildDefaultHtmlBody(ProbeTaskSnapshot snapshot)
        {
            string accent = AccentColor(snapshot);

            StringBuilder html = new();

            // 标题条：一眼看出严重程度，第二行是状态原文。
            html.Append($"<tr><td style=\"padding:20px 24px;background:{accent};color:#ffffff\">");
            html.Append($"<div style=\"font-size:18px;font-weight:600;line-height:1.3\">{Encode(AppInfo.ProductName)} {Encode(Resources.NotificationAlertTitle)}</div>");
            html.Append($"<div style=\"margin-top:4px;font-size:14px;line-height:1.4;opacity:.92\">{Encode(snapshot.StatusText)}</div>");
            html.Append("</td></tr>");

            html.Append("<tr><td style=\"padding:6px 24px 18px\"><table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"border-collapse:collapse\">");

            (string Label, string Value)[] fields = DefaultFields(snapshot);

            for (int i = 0; i < fields.Length; i++)
                AppendHtmlRow(html, fields[i].Label, fields[i].Value, last: i == fields.Length - 1);

            html.Append("</table></td></tr>");

            html.Append($"<tr><td style=\"padding:12px 24px;background:#f9fafb;border-top:1px solid #eaecf0;font-size:12px;line-height:1.5;color:#667085\">{Encode(AppInfo.VersionText)}</td></tr>");

            return WrapInCard(html.ToString());
        }

        /// <summary>
        /// 只配了纯文本模板时，用最小外壳把它变成 HTML：正文按纯文本转义，换行转成 <c>&lt;br&gt;</c>。
        /// </summary>
        /// <remarks>不套内置的告警卡片：用户自己写了正文，就不该再被塞进一堆他没要求的字段。</remarks>
        private static string WrapPlainText(string text)
        {
            string body = Encode(text).Replace("\r\n", "<br>", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);

            return WrapInCard($"<tr><td style=\"padding:24px;font-size:14px;line-height:1.6\">{body}</td></tr>");
        }

        /// <summary>
        /// 邮件外壳：单列卡片。只用 table + 内联样式——邮件客户端会剥掉 <c>&lt;style&gt;</c>，也不保证支持 flex / grid。
        /// </summary>
        private static string WrapInCard(string inner)
        {
            StringBuilder html = new();
            html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>");
            html.Append($"<body style=\"margin:0;padding:24px 12px;background:#f2f4f7;font-family:{FontFamily};color:#101828\">");
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"max-width:640px;margin:0 auto;border-collapse:separate;background:#ffffff;border-radius:12px;overflow:hidden\">");
            html.Append(inner);
            html.Append("</table></body></html>");

            return html.ToString();
        }

        /// <summary>标题条颜色：成功用绿色，其余按严重程度取色，避免恢复通知也显示成红色。</summary>
        private static string AccentColor(ProbeTaskSnapshot snapshot)
        {
            if (snapshot.IsSuccess)
                return "#12b76a";

            return snapshot.Severity switch
            {
                ProbeSeverity.Critical => "#d92d20",
                ProbeSeverity.Warning => "#dc6803",
                ProbeSeverity.Info => "#1570ef",
                _ => "#475467"
            };
        }

        private static string FormatRate(double rate)
            => (rate * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";

        private static void AppendTextLine(StringBuilder builder, string label, string value)
            => builder.AppendLine(string.Format(CultureInfo.CurrentCulture, Resources.NotificationLineFormat, label, value));

        private static void AppendHtmlRow(StringBuilder builder, string label, string value, bool last = false)
        {
            string border = last ? string.Empty : "border-bottom:1px solid #f2f4f7;";

            builder.Append("<tr>");
            builder.Append($"<td style=\"padding:10px 0;width:136px;vertical-align:top;font-size:14px;line-height:1.5;color:#667085;{border}\">{Encode(label)}</td>");
            builder.Append($"<td style=\"padding:10px 0;vertical-align:top;font-size:14px;line-height:1.5;white-space:pre-wrap;word-break:break-word;{border}\">{Encode(value)}</td>");
            builder.Append("</tr>");
        }

        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
