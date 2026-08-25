namespace SquadTools;

internal sealed record MapLayerSelection(string Map, string Layer)
{
    internal string Url =>
        $"http://game.slyw.me/?map={Uri.EscapeDataString(Map)}&layer={Uri.EscapeDataString(Layer)}";
}
