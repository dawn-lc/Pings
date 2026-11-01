using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Pings.Properties
{
    /// <summary>
    /// 从**主程序集的内嵌资源**里解析本地化字符串，取代 <see cref="ResourceManager" />。
    /// </summary>
    /// <remarks>
    /// 为什么不用 <see cref="ResourceManager" />：它只在**卫星程序集**（<c>zh-CN\Pings.resources.dll</c>）
    /// 里查找 culture 资源，不会去主程序集找。而 Native AOT **无法把卫星程序集打进 exe**，
    /// 于是 AOT 产物必然是「exe + 若干语言目录」——与「单文件下载」的发布形态冲突。
    ///
    /// 做法：各语言的 <c>.resources</c> 用 <c>LogicalName</c> 直接编进主程序集，
    /// 资源名约定为 <c>{BaseName}.{culture}.resources</c>（中性为 <c>{BaseName}.resources</c>），
    /// 由本类按 culture 链自行查找。这样无论常规构建还是 AOT，产物都只有一个 exe。
    ///
    /// 每个语言只在首次使用时读一次并**完整物化成字典**，随后关掉 <see cref="ResourceSet" />：
    /// <see cref="ResourceSet" /> 会一直持有底层 <see cref="Stream" /> 的读取器，
    /// 保留它就意味着必须让流活着，是个容易踩的生命周期坑；一张静态的字典更简单也更快。
    /// </remarks>
    public static class EmbeddedResourceManager
    {
        /// <summary>中性资源名（与 csproj 里 <c>LogicalName</c> 的前缀一致）。</summary>
        private const string BaseName = "Pings.Properties.Resources";

        /// <summary>本程序的中性语言：找不到任何文化资源时回退到它。</summary>
        private static readonly CultureInfo NeutralCulture = CultureInfo.InvariantCulture;

        /// <summary>
        /// 简体中文别名归一。系统可能报 <c>zh-Hans-CN</c> / <c>zh-CHS</c> / <c>zh-Hans</c>，
        /// 而资源文件只有 <c>zh-CN</c> 一套——不归一就会静默回退成英文界面。
        /// 繁体用户也落到简体（比看英文更可用）。
        /// </summary>
        private static readonly Dictionary<string, string> CultureAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = "zh-CN",
            ["zh-Hans"] = "zh-CN",
            ["zh-Hans-CN"] = "zh-CN",
            ["zh-CHS"] = "zh-CN",
            ["zh-SG"] = "zh-CN",
            ["zh-Hant"] = "zh-CN",
            ["zh-Hant-TW"] = "zh-CN",
            ["zh-Hant-HK"] = "zh-CN",
            ["zh-TW"] = "zh-CN",
            ["zh-HK"] = "zh-CN",
            ["zh-CHT"] = "zh-CN"
        };

        /// <summary>已加载的资源表缓存（键为 culture 名，中性为空串）。线程安全。</summary>
        private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>?> Cache = new(StringComparer.Ordinal);

        private static readonly Assembly OwnerAssembly = typeof(EmbeddedResourceManager).Assembly;

        /// <summary>
        /// 读取本地化字符串，按 culture → 别名 → Parent → … → 中性 的顺序查找。
        /// </summary>
        /// <param name="name">资源键。</param>
        /// <param name="culture">
        /// 目标语言；为 <see langword="null" /> 时使用 <see cref="CultureInfo.CurrentUICulture" />，
        /// 与原 <c>ResourceManager.GetString(name, culture)</c> 的语义一致。
        /// 少了这层回退，调用方传 null 就会永远只出一套语言。
        /// </param>
        /// <returns>找到的文本；所有候选都缺失时返回 <see langword="null" />（便于测试发现漏键）。</returns>
        public static string? GetString(string name, CultureInfo? culture)
        {
            CultureInfo effective = culture ?? CultureInfo.CurrentUICulture;

            foreach (string candidate in EnumerateCandidates(effective))
            {
                IReadOnlyDictionary<string, string>? table = Cache.GetOrAdd(candidate, Load);

                if (table is not null && table.TryGetValue(name, out string? value))
                    return value;
            }

            return null;
        }

        /// <summary>当前 UI 语言下是否存在该键。</summary>
        public static bool HasKey(string name) => GetString(name, null) is not null;

        /// <summary>
        /// 按优先级列出要尝试的 culture 名：先原名，再别名，然后逐级 Parent，最后中性。
        /// </summary>
        /// <remarks>
        /// 不用 <c>CultureInfo.Parent</c> 直接遍历是因为 <c>InvariantCulture.Parent</c> 指向自身，
        /// 会死循环；这里的列表天然终止且去重。
        /// </remarks>
        private static List<string> EnumerateCandidates(CultureInfo? culture)
        {
            List<string> candidates = [];

            void Add(string? name)
            {
                string key = string.IsNullOrEmpty(name) ? string.Empty : name!;

                if (!candidates.Contains(key, StringComparer.Ordinal))
                    candidates.Add(key);
            }

            if (culture is not null && !culture.Equals(NeutralCulture))
            {
                Add(culture.Name);

                if (CultureAliases.TryGetValue(culture.Name, out string? alias))
                    Add(alias);

                // zh-Hans-CN -> zh-Hans -> zh：逐级归一，再走别名表
                for (CultureInfo? parent = culture.Parent;
                     parent is not null && !parent.Equals(NeutralCulture);
                     parent = parent.Parent)
                {
                    Add(parent.Name);

                    if (CultureAliases.TryGetValue(parent.Name, out string? parentAlias))
                        Add(parentAlias);
                }
            }

            Add(string.Empty);   // 中性兜底
            return candidates;
        }

        /// <summary>读取并物化一个语言的资源表；资源不存在时返回 <see langword="null" />。</summary>
        private static IReadOnlyDictionary<string, string>? Load(string cultureName)
        {
            string resourceName = cultureName.Length == 0
                ? $"{BaseName}.resources"
                : $"{BaseName}.{cultureName}.resources";

            using Stream? stream = OwnerAssembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return null;

            using ResourceSet set = new(stream);
            Dictionary<string, string> table = new(StringComparer.Ordinal);

            // 用 GetEnumerator（内部会一次性 ReadResources）而不是逐个 GetString：
            // 这样之后不再依赖底层流，ResourceSet 可以立刻释放。
            foreach (DictionaryEntry entry in set)
            {
                if (entry.Key is string key && entry.Value is string value)
                    table[key] = value;
            }

            return table;
        }
    }
}
