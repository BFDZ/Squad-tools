using System.Text;
using System.Text.RegularExpressions;

namespace SquadTools;

internal sealed class SquadLogReader : IDisposable
{
    private static readonly Regex MapLayerPattern = new(
        @"/(?<mapPath>[^/\s]+)/Maps/Gameplay_Layers/(?<layerName>[^.\s]+)\.\k<layerName>:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        refreshTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
    }

    private void ReadLatestSelection(bool forceNotify = false)
    {
        if (disposed)
        {
            return;
        }

        if (!File.Exists(logPath))
        {
            ReportStatus("未找到 Squad 地图日志，等待游戏启动");
            return;
        }

        try
        {
            MapLayerSelection? selection = null;
            using FileStream stream = new(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream, new UTF8Encoding(false, true));
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                MapLayerSelection? parsed = Parse(line);
                if (parsed is not null)
                {
                    selection = parsed;
                }
            }

            if (selection is null)
            {
                ReportStatus("等待识别地图和 Layer");
                return;
            }

            bool changed;
            lock (syncRoot)
            {
                changed = forceNotify || latestSelection != selection;
                latestSelection = selection;
            }

            if (changed)
            {
                MapLayerChanged?.Invoke(selection);
            }

            ReportStatus($"已识别：{selection.Map} / {selection.Layer}");
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

    private static MapLayerSelection? Parse(string line)
    {
        Match match = MapLayerPattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        string map = match.Groups["mapPath"].Value.Replace("_", string.Empty, StringComparison.Ordinal);
        string fullLayer = match.Groups["layerName"].Value;
        string prefix = map + "_";
        string layer = fullLayer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fullLayer[prefix.Length..]
            : fullLayer;
        layer = layer.Replace("_", string.Empty, StringComparison.Ordinal);

        return map.Length == 0 || layer.Length == 0 ? null : new MapLayerSelection(map, layer);
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
