using System.Text;
using System.Text.RegularExpressions;

namespace SquadTools;

internal sealed class SquadLogReader : IDisposable
{
    private static readonly Regex MapLayerPattern = new(
        @"/Game/Maps/(?<mapName>[^/:\s]+)/Gameplay_Layers/(?<layerName>[^.\s]+)\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FactionSetupPattern = new(
        @"Success to load FactionSetup\s+(?<setup>\S+)\s+for team\s+(?<team>[12])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LoadedFactionPattern = new(
        @"Loaded Faction\s*:\s*(?<faction>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SquadGame",
        "Saved",
        "Logs",
        "SquadGame.log");
    private readonly FileSystemWatcher watcher;
    private readonly System.Threading.Timer refreshTimer;
    private readonly object syncRoot = new();
    private MapLayerSelection? latestSelection;
    private DateTime lastWriteUtc = DateTime.MinValue;
    private long lastLength = -1;
    private bool disposed;

    internal event Action<MapLayerSelection>? MapLayerChanged;
    internal event Action<string>? StatusChanged;

    internal void ScanNow()
    {
        ReadLatestSelection(true);
    }

    internal SquadLogReader()
    {
        string? directory = Path.GetDirectoryName(logPath);
        if (directory is null)
        {
            throw new InvalidOperationException("无法确定 Squad 日志目录。");
        }

        Directory.CreateDirectory(directory);
        watcher = new FileSystemWatcher(directory!, "SquadGame.log")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = Directory.Exists(directory)
        };
        watcher.Changed += ScheduleRefresh;
        watcher.Created += ScheduleRefresh;
        watcher.Renamed += ScheduleRefresh;
        watcher.Error += (_, _) => ReportStatus("日志监听发生错误，将继续定时检查");
        refreshTimer = new System.Threading.Timer(_ => ReadLatestSelection(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    private void ScheduleRefresh(object? sender, FileSystemEventArgs e)
    {
        // 快速响应日志变化，同时恢复两秒周期轮询（FileSystemWatcher 可能丢事件）。
        refreshTimer.Change(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(2));
    }

    private void ReadLatestSelection(bool forceNotify = false)
    {
        if (disposed)
        {
            return;
        }

        FileInfo info = new(logPath);
        if (!info.Exists)
        {
            lock (syncRoot)
            {
                latestSelection = null;
                lastWriteUtc = DateTime.MinValue;
                lastLength = -1;
            }

            ReportStatus("未找到 Squad 地图日志，等待游戏启动");
            return;
        }

        if (info.LastWriteTimeUtc == lastWriteUtc && info.Length == lastLength)
        {
            NotifyCached(forceNotify);
            return;
        }

        try
        {
            MapLayerSelection? selection = null;
            MapLayerSelection? current = null;
            string? team1Unit = null;
            string? team2Unit = null;
            List<string> loadedFactions = [];
            using FileStream stream = new(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream, new UTF8Encoding(false, true));
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                MapLayerSelection? parsed = ParseMapLayer(line);
                if (parsed is not null)
                {
                    if (current is null || current != parsed)
                    {
                        // 进入新地图：重置阵营，等待该图的阵营配置日志。
                        current = parsed;
                        team1Unit = null;
                        team2Unit = null;
                        loadedFactions.Clear();
                    }

                    selection = parsed;
                    continue;
                }

                ParseFaction(line, ref team1Unit, ref team2Unit, loadedFactions);
            }

            // 未出现 FactionSetup 配置时，按阵营加载顺序（先 team1 后 team2）回退。
            if (team1Unit is null && team2Unit is null && loadedFactions.Count > 0)
            {
                team1Unit = loadedFactions[0];
                team2Unit = loadedFactions.Count > 1 ? loadedFactions[1] : null;
            }

            if (selection is not null && (team1Unit is not null || team2Unit is not null))
            {
                selection = selection with { Team1Unit = team1Unit, Team2Unit = team2Unit };
            }

            bool changed;
            lock (syncRoot)
            {
                changed = selection is not null && (forceNotify || latestSelection != selection);
                latestSelection = selection;
                lastWriteUtc = info.LastWriteTimeUtc;
                lastLength = info.Length;
            }

            if (selection is null)
            {
                ReportStatus("等待识别地图和 Layer");
                return;
            }

            if (changed)
            {
                MapLayerChanged?.Invoke(selection);
            }

            ReportStatus(BuildStatus(selection));
        }
        catch (DecoderFallbackException)
        {
            ReportStatus("无法读取地图日志编码，等待下次检查");
        }
        catch (IOException)
        {
            ReportStatus("地图日志暂时被游戏占用，等待下次检查");
        }
        catch (UnauthorizedAccessException)
        {
            ReportStatus("没有权限读取 Squad 地图日志");
        }
    }

    private void NotifyCached(bool forceNotify)
    {
        if (!forceNotify)
        {
            return;
        }

        MapLayerSelection? cached;
        lock (syncRoot)
        {
            cached = latestSelection;
        }

        if (cached is null)
        {
            ReportStatus("等待识别地图和 Layer");
            return;
        }

        MapLayerChanged?.Invoke(cached);
        ReportStatus(BuildStatus(cached));
    }

    private static MapLayerSelection? ParseMapLayer(string line)
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

        string mapDirectory = match.Groups["mapName"].Value;
        string fullLayer = match.Groups["layerName"].Value;
        string layer = fullLayer.StartsWith(mapDirectory + "_", StringComparison.OrdinalIgnoreCase)
            ? fullLayer[(mapDirectory.Length + 1)..]
            : fullLayer;
        string map = mapDirectory.Replace("_", string.Empty, StringComparison.Ordinal);
        layer = layer.Replace("_", string.Empty, StringComparison.Ordinal);

        return map.Length == 0 || layer.Length == 0 ? null : new MapLayerSelection(map, layer);
    }

    private static void ParseFaction(string line, ref string? team1Unit, ref string? team2Unit, List<string> loadedFactions)
    {
        if (!line.Contains("LogSquad:", StringComparison.Ordinal))
        {
            return;
        }

        Match setup = FactionSetupPattern.Match(line);
        if (setup.Success)
        {
            switch (setup.Groups["team"].Value)
            {
                case "1": team1Unit = setup.Groups["setup"].Value; break;
                case "2": team2Unit = setup.Groups["setup"].Value; break;
            }

            return;
        }

        Match loaded = LoadedFactionPattern.Match(line);
        if (loaded.Success)
        {
            string faction = loaded.Groups["faction"].Value;
            if (!loadedFactions.Contains(faction))
            {
                loadedFactions.Add(faction);
            }
        }
    }

    private static string BuildStatus(MapLayerSelection selection)
    {
        string team1 = MapLayerSelection.FactionName(selection.Team1Unit);
        string team2 = MapLayerSelection.FactionName(selection.Team2Unit);
        return team1.Length == 0 && team2.Length == 0
            ? $"已识别：{selection.Map} / {selection.Layer}"
            : $"已识别：{selection.Map} / {selection.Layer}（{team1} vs {team2}）";
    }

    private void ReportStatus(string status)
    {
        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        watcher.Dispose();
        refreshTimer.Dispose();
    }
}
