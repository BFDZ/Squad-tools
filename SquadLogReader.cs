using System.Text;
using System.Text.RegularExpressions;

namespace SquadTools;

internal sealed class SquadLogReader : IDisposable
{
    private static readonly Regex FactionSetupPattern = new(
        @"Success to load FactionSetup\s+(?<setup>\S+)\s+for team\s+(?<team>[12])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LoadedFactionPattern = new(
        @"Loaded Faction\s*:\s*(?<faction>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string logPath;
    private readonly FileSystemWatcher watcher;
    private readonly System.Threading.Timer refreshTimer;
    private readonly object syncRoot = new();
    private readonly List<string> scanFactions = [];
    private MapLayerSelection? latestSelection;

    // 增量扫描状态：只在扫描线程内读写。
    private MapLayerSelection? scanSelection;
    private string? scanTeam1Unit;
    private string? scanTeam2Unit;
    private long consumedBytes;
    private byte[] pendingBytes = [];
    private DateTime scanCreationUtc = DateTime.MinValue;
    private int scanning;
    private bool disposed;

    internal event Action<MapLayerSelection>? MapLayerChanged;
    internal event Action<string>? StatusChanged;

    internal void ScanNow()
    {
        ReadLatestSelection(true);
    }

    internal SquadLogReader(string? logPathOverride = null)
    {
        logPath = logPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadGame",
            "Saved",
            "Logs",
            "SquadGame.log");

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

        // 定时器与文件监听可能同时触发；上一次扫描未结束时直接跳过，避免状态错乱。
        if (Interlocked.CompareExchange(ref scanning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            ReadLatestSelectionCore(forceNotify);
        }
        finally
        {
            Volatile.Write(ref scanning, 0);
        }
    }

    private void ReadLatestSelectionCore(bool forceNotify)
    {
        FileInfo info = new(logPath);
        if (!info.Exists)
        {
            ResetScanState();
            lock (syncRoot)
            {
                latestSelection = null;
            }

            ReportStatus("未找到 Squad 地图日志，等待游戏启动");
            return;
        }

        long length;
        DateTime creationUtc;
        try
        {
            info.Refresh();
            length = info.Length;
            creationUtc = info.CreationTimeUtc;
        }
        catch (IOException)
        {
            ReportStatus("地图日志暂时被游戏占用，等待下次检查");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ReportStatus("没有权限读取 Squad 地图日志");
            return;
        }

        if (creationUtc != scanCreationUtc)
        {
            // 日志被重建或轮换（游戏重启会新建文件），需要从头解析。
            ResetScanState();
            scanCreationUtc = creationUtc;
        }

        if (length == consumedBytes && pendingBytes.Length == 0)
        {
            NotifyCached(forceNotify);
            return;
        }

        byte[] chunk;
        try
        {
            using FileStream stream = new(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < consumedBytes)
            {
                // 文件被截断（清空日志或换新文件但创建时间未变），从头再来。
                ResetScanState();
            }

            stream.Seek(consumedBytes, SeekOrigin.Begin);
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            chunk = buffer.ToArray();
        }
        catch (IOException)
        {
            ReportStatus("地图日志暂时被游戏占用，等待下次检查");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ReportStatus("没有权限读取 Squad 地图日志");
            return;
        }

        if (chunk.Length == 0 && pendingBytes.Length == 0)
        {
            NotifyCached(forceNotify);
            return;
        }

        // 与上次残留的半行拼接，只解析已换行结束的完整行；末尾不完整的部分留到下次。
        byte[] combined = new byte[pendingBytes.Length + chunk.Length];
        pendingBytes.CopyTo(combined, 0);
        chunk.CopyTo(combined, pendingBytes.Length);

        long start = consumedBytes;
        int lastNewline = Array.LastIndexOf(combined, (byte)'\n');
        string completeText;
        if (lastNewline < 0)
        {
            pendingBytes = combined;
            consumedBytes = start + chunk.Length;
            completeText = string.Empty;
        }
        else
        {
            completeText = Encoding.UTF8.GetString(combined, 0, lastNewline + 1);
            pendingBytes = combined[(lastNewline + 1)..];
            consumedBytes = start + lastNewline + 1;
        }

        foreach (string rawLine in completeText.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length > 0)
            {
                ApplyLine(line);
            }
        }

        PublishSelection(forceNotify);
    }

    /// <summary>把关卡/阵营行喂给增量扫描状态。</summary>
    private void ApplyLine(string line)
    {
        MapLayerSelection? parsed = LayerNameParser.TryParse(line);
        if (parsed is not null)
        {
            if (scanSelection is null || scanSelection != parsed)
            {
                // 进入新地图：重置阵营，等待该图层自己的阵营配置日志。
                scanSelection = parsed;
                scanTeam1Unit = null;
                scanTeam2Unit = null;
                scanFactions.Clear();
            }

            return;
        }

        ParseFaction(line, ref scanTeam1Unit, ref scanTeam2Unit, scanFactions);
    }

    private void PublishSelection(bool forceNotify)
    {
        string? team1Unit = scanTeam1Unit;
        string? team2Unit = scanTeam2Unit;

        // 未出现 FactionSetup 配置时，按阵营加载顺序（先 team1 后 team2）回退。
        if (team1Unit is null && team2Unit is null && scanFactions.Count > 0)
        {
            team1Unit = scanFactions[0];
            team2Unit = scanFactions.Count > 1 ? scanFactions[1] : null;
        }

        MapLayerSelection? selection = scanSelection;
        if (selection is not null && (team1Unit is not null || team2Unit is not null))
        {
            selection = selection with { Team1Unit = team1Unit, Team2Unit = team2Unit };
        }

        bool changed;
        lock (syncRoot)
        {
            changed = selection is not null && (forceNotify || latestSelection != selection);
            latestSelection = selection;
        }

        if (selection is null)
        {
            ReportStatus("等待识别地图和 Layer");
            return;
        }

        if (changed && !disposed)
        {
            MapLayerChanged?.Invoke(selection);
        }

        ReportStatus(BuildStatus(selection));
    }

    private void ResetScanState()
    {
        consumedBytes = 0;
        pendingBytes = [];
        scanSelection = null;
        scanTeam1Unit = null;
        scanTeam2Unit = null;
        scanCreationUtc = DateTime.MinValue;
        scanFactions.Clear();
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

        if (!disposed)
        {
            MapLayerChanged?.Invoke(cached);
        }

        ReportStatus(BuildStatus(cached));
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
