using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pings.Configuration
{
    /// <summary>
    /// 源生成的 JSON 上下文（Native AOT 下必需）。
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(List<IcmpTaskConfigJson>))]
    [JsonSerializable(typeof(IcmpTaskConfigJson))]
    [JsonSerializable(typeof(GlobalConfig))]
    [JsonSerializable(typeof(LoggingConfig))]
    [JsonSerializable(typeof(NotificationsConfig))]
    [JsonSerializable(typeof(WebhookConfig))]
    [JsonSerializable(typeof(EmailConfig))]
    internal partial class JsonContext : JsonSerializerContext
    {
        private static readonly Lazy<JsonContext> relaxed = new(() =>
            new JsonContext(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }));

        /// <summary>宽松读写用的上下文：中文不转义、容忍注释与尾随逗号。</summary>
        public static JsonContext Relaxed => relaxed.Value;
    }
}
