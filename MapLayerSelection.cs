namespace SquadTools;

internal sealed record MapLayerSelection(string Map, string Layer, string? Team1Unit = null, string? Team2Unit = null)
{
    /// <summary>
    /// 地图工具页面使用的查询串。页面本身由本地 MapHost 提供，
    /// 这里只负责拼装 ?map=..&layer=..，不再依赖任何远程站点域名。
    /// </summary>
    internal string Query
    {
        get
        {
            string query = $"map={Uri.EscapeDataString(Map)}&layer={Uri.EscapeDataString(Layer)}";
            if (Team1Unit is not null)
            {
                query += $"&team1={Uri.EscapeDataString(Team1Unit)}";
            }

            if (Team2Unit is not null)
            {
                query += $"&team2={Uri.EscapeDataString(Team2Unit)}";
            }

            return $"?{query}";
        }
    }

    internal static string FactionName(string? unit)
    {
        if (unit is null)
        {
            return string.Empty;
        }

        int separator = unit.IndexOf('_', StringComparison.Ordinal);
        return separator > 0 ? unit[..separator] : unit;
    }
}
