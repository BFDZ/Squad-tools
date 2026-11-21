using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SquadTools;

internal sealed class MapGuessForm : Form
{
    private static readonly Regex CoordinatePattern = new(
        @"^[^:]*:\s*(?<elevation>.+?)\s*-\s*[^:]*:\s*(?<bearing>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly WebView2 browser = new();
    private readonly Label statusLabel = new();
    private readonly Label messageLabel = new();
    private readonly Icon applicationIcon;
    private readonly ProxySettings proxySettings;
    private readonly string mapHostDirectory;
    private LocalMapHostServer? localServer;
    private TargetOverlayForm? targetOverlay;
    private bool allowClose;
    private bool initialized;
    private string? pendingQuery;
    private MapLayerSelection? appliedSelection;
    private ulong activeNavigationId;
    private bool waitingForPage;
    private string? lastNavigatedUrl;
    private string? inflightUrl;

    internal event Action? WindowShown;

    internal MapGuessForm(Icon icon, ProxySettings proxySettings, string mapHostDirectory)
    {
        this.proxySettings = proxySettings;
        this.mapHostDirectory = mapHostDirectory;
        Text = "地图工具";
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(720, 480);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        applicationIcon = (Icon)icon.Clone();
        Icon = applicationIcon;

        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 28;
        statusLabel.Padding = new Padding(10, 0, 0, 0);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Text = "等待 Squad 地图日志";
        statusLabel.BackColor = Color.FromArgb(245, 245, 245);
        statusLabel.ForeColor = Color.FromArgb(70, 75, 80);

        browser.Dock = DockStyle.Fill;
        browser.BackColor = Color.FromArgb(245, 245, 245);
        browser.NavigationStarting += OnNavigationStarting;
        browser.NavigationCompleted += OnNavigationCompleted;

        messageLabel.Dock = DockStyle.Fill;
        messageLabel.TextAlign = ContentAlignment.MiddleCenter;
        messageLabel.Padding = new Padding(40, 0, 40, 0);
        messageLabel.BackColor = Color.FromArgb(245, 245, 245);
        messageLabel.ForeColor = Color.FromArgb(150, 45, 45);
        messageLabel.Visible = false;

        Controls.Add(browser);
        Controls.Add(messageLabel);
        Controls.Add(statusLabel);
        Load += (_, _) => SetInitialBounds();
        Shown += (_, _) => InitializeBrowser();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
            targetOverlay?.Dispose();
            localServer?.Dispose();
            browser.Dispose();
            applicationIcon.Dispose();
        };
    }

    private void SetInitialBounds()
    {
        Rectangle workingArea = Screen.FromHandle(Handle).WorkingArea;
        int width = Math.Max(MinimumSize.Width, workingArea.Width * 3 / 4);
        int height = Math.Max(MinimumSize.Height, workingArea.Height * 3 / 4);
        width = Math.Min(width, workingArea.Width);
        height = Math.Min(height, workingArea.Height);

        Bounds = new Rectangle(
            workingArea.Left + (workingArea.Width - width) / 2,
            workingArea.Top + (workingArea.Height - height) / 2,
            width,
            height);
    }

    internal void ApplyMapLayer(MapLayerSelection selection)
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(() => ApplyMapLayer(selection));
            return;
        }

        pendingQuery = selection.Query;
        string team1 = MapLayerSelection.FactionName(selection.Team1Unit);
        string team2 = MapLayerSelection.FactionName(selection.Team2Unit);
        string factions = team1.Length == 0 && team2.Length == 0
            ? string.Empty
            : $"    阵营：{team1} vs {team2}";
        statusLabel.Text = $"当前地图：{selection.Map}    Layer：{selection.Layer}{factions}";
        if (appliedSelection == selection)
        {
            return;
        }

        appliedSelection = selection;
        if (initialized && browser.CoreWebView2 is not null)
        {
            string target = BuildLocalUrl(pendingQuery);
            inflightUrl = target;
            browser.CoreWebView2.Navigate(target);
        }
    }

    internal void UpdateLogStatus(string status)
    {
        if (!IsDisposed && IsHandleCreated)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateLogStatus(status));
                return;
            }

            statusLabel.Text = status;
        }
    }

    internal void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        WindowShown?.Invoke();
        EnsureBrowserShowsPendingMap();
    }

    // 安全网：窗口隐藏期间发起的导航若未完成（如 WebView2 挂起），
    // 重新打开时按“最近成功加载的地址”判断是否需要重新加载最新地图。
    private void EnsureBrowserShowsPendingMap()
    {
        if (!initialized || browser.CoreWebView2 is null || localServer is null || pendingQuery is null)
        {
            return;
        }

        string target = BuildLocalUrl(pendingQuery);
        if (string.Equals(lastNavigatedUrl, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(inflightUrl, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        inflightUrl = target;
        browser.CoreWebView2.Navigate(target);
    }

    internal void CloseWindow()
    {
        allowClose = true;
        Close();
    }

    private async void InitializeBrowser()
    {
        if (initialized || IsDisposed)
        {
            return;
        }

        try
        {
            string dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SquadTools",
                "WebView2");
            CoreWebView2EnvironmentOptions options = new();
            if (proxySettings.Enabled)
            {
                options.AdditionalBrowserArguments = $"--proxy-server={proxySettings.CommandLineValue}";
            }

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, dataDirectory, options);
            await browser.EnsureCoreWebView2Async(environment);
            localServer = LocalMapHostServer.Start(mapHostDirectory);
            targetOverlay = new TargetOverlayForm();
            browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            initialized = true;
            HideMessage();
            string initialUrl = BuildLocalUrl(pendingQuery);
            inflightUrl = initialUrl;
            browser.CoreWebView2.Navigate(initialUrl);
        }
        catch (Exception exception)
        {
            statusLabel.Text = $"地图工具网页初始化失败：{exception.Message}";
            ShowMessage(DescribeInitializationFailure(exception));
        }
    }

    private void ShowMessage(string text)
    {
        messageLabel.Text = text;
        messageLabel.Visible = true;
        messageLabel.BringToFront();
    }

    private void HideMessage()
    {
        if (messageLabel.Visible)
        {
            messageLabel.Visible = false;
            browser.BringToFront();
        }
    }

    private string DescribeInitializationFailure(Exception exception)
    {
        if (exception is DirectoryNotFoundException)
        {
            return "地图工具资源缺失：" + Environment.NewLine
                + $"未找到 {mapHostDirectory}" + Environment.NewLine + Environment.NewLine
                + "请确认程序目录下的 MapHost 文件夹完整（含 index.html、img、src），"
                + "或重新解压完整的发布包。";
        }

        return "地图工具网页初始化失败：" + Environment.NewLine + Environment.NewLine
            + exception.Message + Environment.NewLine + Environment.NewLine
            + "若提示与 WebView2 相关，请安装 Microsoft Edge WebView2 Runtime；"
            + "若在“网络设置”中启用了代理，请确认代理可用后重启程序。";
    }

    private string BuildLocalUrl(string? query)
    {
        return $"{localServer!.BaseUrl}index.html{query}";
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out JsonElement type)
                || type.GetString() != "squadtools-targets")
            {
                return;
            }

            List<string> lines = [];
            if (root.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                List<JsonElement> targets = [.. items.EnumerateArray()];
                foreach (JsonElement item in targets.Skip(Math.Max(0, targets.Count - 10)))
                {
                    if (!item.TryGetProperty("text", out JsonElement texts) || texts.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    List<string> entries = [];
                    foreach (JsonElement text in texts.EnumerateArray())
                    {
                        string? value = text.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            entries.Add(FormatCoordinate(value));
                        }
                    }

                    if (entries.Count <= 1)
                    {
                        lines.AddRange(entries);
                    }
                    else
                    {
                        for (int i = 0; i < entries.Count; i++)
                        {
                            lines.Add($"{i + 1}. {entries[i]}");
                        }
                    }
                }
            }

            targetOverlay?.ShowTargets(lines);
        }
        catch (JsonException)
        {
            // 忽略格式异常的网页消息
        }
        catch (InvalidOperationException)
        {
            // WebView2 已释放时忽略
        }
    }

    internal static string FormatCoordinate(string text)
    {
        Match match = CoordinatePattern.Match(text);
        return match.Success
            ? $"{match.Groups["elevation"].Value}，{match.Groups["bearing"].Value}"
            : text;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        activeNavigationId = e.NavigationId;
        waitingForPage = !string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase);
        inflightUrl = waitingForPage ? e.Uri : null;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.NavigationId != activeNavigationId || !waitingForPage)
        {
            return;
        }

        if (e.IsSuccess)
        {
            lastNavigatedUrl = inflightUrl;
            inflightUrl = null;
            targetOverlay?.ClearTargets();
            return;
        }

        inflightUrl = null;
        statusLabel.Text = $"地图工具网页加载失败：{e.WebErrorStatus}";
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
