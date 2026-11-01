using System.Globalization;
using System.Text;

namespace Pings.Notifications
{
    /// <summary>
    /// 单遍模板替换，支持 <c>%#Key#%</c> 与 <c>%#Key:格式#%</c>（格式用于时间值）。
    /// </summary>
    /// <remarks>
    /// 旧实现使用「替换后再扫描」的循环，既慢又依赖 seen 集合防循环；
    /// 单遍扫描不会有自引用问题，占位符未命中时原样保留。
    /// </remarks>
    public static class TemplateRenderer
    {
        public const string Prefix = "%#";
        public const string Suffix = "#%";

        public static string Render(string? template, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template) || values.Count == 0)
                return template ?? string.Empty;

            StringBuilder builder = new(template.Length);

            int position = 0;
            while (position < template.Length)
            {
                int start = template.IndexOf(Prefix, position, StringComparison.Ordinal);
                if (start < 0)
                {
                    builder.Append(template, position, template.Length - position);
                    break;
                }

                int end = template.IndexOf(Suffix, start + Prefix.Length, StringComparison.Ordinal);
                if (end < 0)
                {
                    builder.Append(template, position, template.Length - position);
                    break;
                }

                builder.Append(template, position, start - position);

                string placeholder = template[(start + Prefix.Length)..end];
                if (!TryResolve(placeholder, values, out string? resolved))
                    resolved = Prefix + placeholder + Suffix;

                builder.Append(resolved);
                position = end + Suffix.Length;
            }

            return builder.ToString();
        }

        private static bool TryResolve(string placeholder, IReadOnlyDictionary<string, string> values, out string resolved)
        {
            resolved = string.Empty;

            int colon = placeholder.IndexOf(':', StringComparison.Ordinal);
            string key = colon > 0 ? placeholder[..colon] : placeholder;
            string? format = colon > 0 ? placeholder[(colon + 1)..] : null;

            if (!values.TryGetValue(key, out string? value))
                return false;

            if (!string.IsNullOrEmpty(format)
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset timestamp))
            {
                try
                {
                    resolved = timestamp.ToString(format, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (FormatException)
                {
                    // 非法格式串时退回原值，避免因配置笔误丢掉通知。
                }
            }

            resolved = value;
            return true;
        }
    }
}
