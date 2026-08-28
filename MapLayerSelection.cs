namespace SquadTools;

internal sealed record MapLayerSelection(string Map, string Layer, string? Team1Unit = null, string? Team2Unit = null)
{
    internal string Url
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

            return $"http://game.slyw.me/?{query}";
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
