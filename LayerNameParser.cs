using System.Text;
using System.Text.RegularExpressions;

namespace SquadTools;

/// <summary>
/// 把 Squad 日志里的关卡资源路径解析成地图工具页面能直接使用的 (地图名, 图层短名)。
///
/// 页面侧（MapHost 内置的 main.js）的匹配规则是：
///   map   —— 与内置地图名 <b>全等</b>（忽略大小写），未命中就随机换一张图；
///   layer —— 与图层下拉项文本 <b>全等</b>（忽略大小写与空格），未命中就清空图层。
/// 下拉项文本来自 calc-api 的 shortName（如 "Invasion v1"），不是日志里的资源名
/// （如 "Tallil_Invasion_v1"），因此这里必须输出 shortName 形态。
/// </summary>
internal static class LayerNameParser
{
    // 日志中的关卡路径有两种形态：
    //   旧式：/Game/Maps/<地图目录>/Gameplay_Layers/<图层资源>.<对象>
    //   新式：/<根>/Maps/Gameplay_Layers/<图层资源>.<对象>（没有地图目录层级）
    // 部分图层目录写作 Gameplay_Layer_Data，这里一并接受。
    // 图层资源名只允许字母/数字/下划线/连字符，并要求后面就是边界，
    // 避免把 "Narva_Invasion_v1)" 这类带括号或杂字符的文本算进名字里。
    private static readonly Regex MapLayerPattern = new(
        @"/(?<root>[^/:\s]+)/Maps/(?:(?<mapName>[^/:\s]+)/)?Gameplay_Layer(?:s|_Data)/(?<layerName>[A-Za-z0-9_\-]+)(?![A-Za-z0-9_\-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // 图层资源名末尾的修饰标记，页面的 shortName 会把它丢掉（如 AAS_v3_CL -> "AAS v3"）。
    private static readonly Regex ModifierTokenPattern = new(
        @"^(?:CL\d*|WP\d*|CAF\d*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex VersionTokenPattern = new(
        @"^v\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // 无前缀可匹配时的兜底：图层资源名里的模式词。
    private static readonly HashSet<string> FallbackModeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RAAS", "AAS", "Invasion", "Skirmish", "TC", "Seed", "Destruction",
        "Insurgency", "Frontline", "Armored", "Coop", "Training"
    };

    // 页面内置的 27 张地图名（必须与 MapHost 的 main.js 完全一致），
    // 以及日志里出现过的资源前缀/地图目录名写法。
    // 例：Tallil 的日志目录是 Tallil_Outskirts，图层前缀却是 Tallil；
    //     Narva 水淹版在页面上叫 Narva_f，日志里是 Narva_Flooded。
    private static readonly (string Page, string[] Prefixes)[] MapTable =
    [
        ("AlBasrah", ["AlBasrah", "Al_Basrah"]),
        ("Anvil", ["Anvil"]),
        ("Belaya", ["Belaya_Pass", "BelayaPass", "Belaya"]),
        ("BlackCoast", ["BlackCoast", "Black_Coast"]),
        ("Chora", ["Chora"]),
        ("Fallujah", ["Fallujah"]),
        ("FoolsRoad", ["FoolsRoad", "Fools_Road"]),
        ("GooseBay", ["GooseBay", "Goose_Bay"]),
        ("Gorodok", ["Gorodok"]),
        ("Harju", ["Harju"]),
        ("Jensen", ["JensensRange", "Jensen"]),
        ("Kamdesh", ["Kamdesh_Highlands", "Kamdesh"]),
        ("Kohat", ["Kohat_Toi", "Kohat"]),
        ("Kokan", ["Kokan"]),
        ("Lashkar", ["Lashkar_Valley", "Lashkar"]),
        ("Logar", ["Logar_Valley", "Logar"]),
        ("Manicouagan", ["Manicouagan"]),
        ("Mestia", ["Mestia"]),
        ("Mutaha", ["Mutaha"]),
        ("Narva", ["Narva"]),
        ("Narva_f", ["Narva_Flooded", "NarvaFlooded", "Narva_f"]),
        ("Pacific", ["PacificProvingGrounds", "Pacific"]),
        ("Sanxian", ["Sanxian_Islands", "Sanxian"]),
        ("Skorpo", ["Skorpo"]),
        ("Sumari", ["Sumari_Bala", "Sumari"]),
        ("Tallil", ["Tallil_Outskirts", "Tallil"]),
        ("Yehorivka", ["Yehorivka"]),
    ];

    private readonly record struct PrefixEntry(string Compact, string Page);

    // 前缀索引：按紧凑长度倒序，保证“Narva_Flooded”优先于“Narva”。
    private static readonly PrefixEntry[] PrefixIndex = BuildPrefixIndex();

    private static PrefixEntry[] BuildPrefixIndex()
    {
        List<PrefixEntry> entries = [];
        foreach ((string page, string[] prefixes) in MapTable)
        {
            foreach (string prefix in prefixes)
            {
                entries.Add(new PrefixEntry(Compact(prefix), page));
            }
        }

        return [.. entries.OrderByDescending(entry => entry.Compact.Length)];
    }

    /// <summary>内置地图名列表，供调用方做存在性校验。</summary>
    internal static IReadOnlyList<string> PageMapNames { get; } =
        [.. MapTable.Select(entry => entry.Page)];

    private static readonly HashSet<string> PageMapNameSet =
        new(PageMapNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 解析一行日志；不是关卡加载行或无法识别时返回 null。
    /// </summary>
    internal static MapLayerSelection? TryParse(string line)
    {
        if (!line.Contains("RegisterComponentWithWorld", StringComparison.Ordinal)
            && !line.Contains("Bringing World", StringComparison.Ordinal))
        {
            return null;
        }

        Match match = MapLayerPattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        string fullLayer = match.Groups["layerName"].Value;
        string? mapDirectory = match.Groups["mapName"].Success ? match.Groups["mapName"].Value : null;

        if (fullLayer.Length == 0)
        {
            return null;
        }

        string[] layerTokens = SplitTokens(fullLayer);

        // 主路径：用已知地图资源前缀切分，图层短名 = 前缀之后的部分。
        if (TryMatchPrefix(layerTokens, out int consumed, out string mapName))
        {
            return Create(mapName, JoinLayerShortName(layerTokens[consumed..]));
        }

        // 退路：地图目录能识别（新地图资源前缀还没登记），地图名用目录，图层按模式词切。
        if (mapDirectory is not null && TryResolveByCompact(mapDirectory, out string directoryMap))
        {
            return Create(directoryMap, SplitByMode(layerTokens));
        }

        // 最后兜底：完全未知的地图，仍按模式词切，但地图名不在页面列表里时丢弃。
        return TrySplitByMode(layerTokens, out string fallbackMap, out string fallbackLayer)
            ? Create(fallbackMap, fallbackLayer)
            : null;
    }

    /// <summary>
    /// 组装结果。地图名必须与页面内置地图名一致，否则页面会随机换图（比不切换更糟），
    /// 因此这里直接丢弃——保留上一局的地图与图层。
    /// </summary>
    private static MapLayerSelection? Create(string mapName, string layerName)
    {
        if (mapName.Length == 0 || layerName.Length == 0 || !PageMapNameSet.Contains(mapName))
        {
            return null;
        }

        return new MapLayerSelection(mapName, layerName);
    }

    /// <summary>
    /// 用已知地图前缀匹配图层资源名的开头，返回被消费的 token 数。
    /// 采用“紧凑字符串”比较，因此 Narva_Flooded 与 NarvaFlooded 都能命中同一条目。
    /// </summary>
    private static bool TryMatchPrefix(string[] layerTokens, out int consumed, out string mapName)
    {
        int best = 0;
        string bestMap = string.Empty;

        foreach (PrefixEntry entry in PrefixIndex)
        {
            string accumulated = string.Empty;
            for (int i = 0; i < layerTokens.Length; i++)
            {
                accumulated += Compact(layerTokens[i]);
                if (accumulated.Length >= entry.Compact.Length)
                {
                    if (accumulated.Length == entry.Compact.Length
                        && string.Equals(accumulated, entry.Compact, StringComparison.Ordinal)
                        && i + 1 > best)
                    {
                        best = i + 1;
                        bestMap = entry.Page;
                    }

                    break;
                }
            }
        }

        consumed = best;
        mapName = bestMap;
        return best > 0;
    }

    /// <summary>把目录名（可能是完整资源目录名）解析成页面地图名。</summary>
    private static bool TryResolveByCompact(string directoryName, out string mapName)
    {
        string compact = Compact(directoryName);
        foreach (PrefixEntry entry in PrefixIndex)
        {
            if (string.Equals(entry.Compact, compact, StringComparison.Ordinal))
            {
                mapName = entry.Page;
                return true;
            }
        }

        // 目录名带额外后缀（如 Tallil_Outskirts 已登记；未知后缀时取最长前缀）。
        string best = string.Empty;
        string bestMap = string.Empty;
        foreach (PrefixEntry entry in PrefixIndex)
        {
            if (compact.StartsWith(entry.Compact, StringComparison.Ordinal) && entry.Compact.Length > best.Length)
            {
                best = entry.Compact;
                bestMap = entry.Page;
            }
        }

        mapName = bestMap;
        return bestMap.Length > 0;
    }

    /// <summary>按模式词切分，返回模式词及其后的内容作为图层短名。</summary>
    private static string SplitByMode(string[] layerTokens)
    {
        int modeIndex = FindModeIndex(layerTokens);
        return modeIndex < 0 ? string.Empty : JoinLayerShortName(layerTokens[modeIndex..]);
    }

    private static bool TrySplitByMode(string[] layerTokens, out string mapName, out string layerName)
    {
        int modeIndex = FindModeIndex(layerTokens);
        if (modeIndex <= 0)
        {
            mapName = string.Empty;
            layerName = string.Empty;
            return false;
        }

        mapName = string.Concat(layerTokens[..modeIndex]);
        layerName = JoinLayerShortName(layerTokens[modeIndex..]);
        return mapName.Length > 0 && layerName.Length > 0;
    }

    private static int FindModeIndex(string[] layerTokens)
    {
        for (int i = 1; i < layerTokens.Length; i++)
        {
            if (FallbackModeNames.Contains(layerTokens[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 拼接图层短名：token 之间用空格，去掉末尾的 CL/WP 之类修饰标记。
    /// 页面上的 shortName 只在模式词后跟版本号时保留模式词（"Invasion v1"）；
    /// 像 JensensRange_Training_USA-PLA 这种没有版本号的训练图层，页面显示为 "USA-PLA"，
    /// 因此这里把“后面不跟版本号”的起始模式词去掉。
    /// </summary>
    private static string JoinLayerShortName(string[] layerTokens)
    {
        int end = layerTokens.Length;
        while (end > 0 && ModifierTokenPattern.IsMatch(layerTokens[end - 1]))
        {
            end--;
        }

        int start = 0;
        if (end - start >= 2
            && FallbackModeNames.Contains(layerTokens[start])
            && !VersionTokenPattern.IsMatch(layerTokens[start + 1]))
        {
            start++;
        }

        return end <= start ? string.Empty : string.Join(' ', layerTokens[start..end]);
    }

    private static string[] SplitTokens(string value) =>
        value.Split('_', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>只保留字母与数字并转小写，用于跨写法比较（Narva_Flooded ≈ NarvaFlooded）。</summary>
    private static string Compact(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
