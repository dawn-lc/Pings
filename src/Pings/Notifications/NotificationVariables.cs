using System.Globalization;
using Pings.Monitoring;

namespace Pings.Notifications
{
    /// <summary>
    /// 通知模板可用的变量表。
    /// </summary>
    /// <remarks>
    /// Webhook 与邮件共用这一份：占位符名字一旦各写一份，迟早出现「Webhook 能用的变量邮件没有」这种莫名差异。
    /// </remarks>
    public static class NotificationVariables
    {
        /// <summary>按当前快照生成模板变量；数值一律用不变文化，避免切换界面语言把小数点变成逗号。</summary>
        public static Dictionary<string, string> Create(ProbeTaskSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            return new Dictionary<string, string>(StringComparer.Ordinal)
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
                ["Jitter"] = snapshot.Statistics.JitterMs is { } jitter ? jitter.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty,
                ["LossRate"] = (snapshot.Statistics.WindowLossRate * 100).ToString("0.0", CultureInfo.InvariantCulture),
                ["TotalLossRate"] = (snapshot.Statistics.TotalLossRate * 100).ToString("0.0", CultureInfo.InvariantCulture),
                ["FailureRate"] = (snapshot.Statistics.WindowFailureRate * 100).ToString("0.0", CultureInfo.InvariantCulture),
                ["TotalFailureRate"] = (snapshot.Statistics.TotalFailureRate * 100).ToString("0.0", CultureInfo.InvariantCulture),
                ["Sent"] = snapshot.Statistics.Sent.ToString(CultureInfo.InvariantCulture),
                ["Received"] = snapshot.Statistics.Received.ToString(CultureInfo.InvariantCulture),
                ["Lost"] = snapshot.Statistics.Lost.ToString(CultureInfo.InvariantCulture),
                ["Failed"] = snapshot.Statistics.Failed.ToString(CultureInfo.InvariantCulture),
                ["LastEvent"] = snapshot.LastEvent,
                ["Warning"] = snapshot.Warning ?? string.Empty,
                ["CurrentTime"] = snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }
    }
}
