using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SquadTools;

internal sealed class MapGuessForm : Form
{
    private const string HomeUrl = "https://squadcalc.app/";
    private readonly WebView2 browser = new();
    private readonly Label statusLabel = new();
    private readonly Icon applicationIcon;
    private readonly ProxySettings proxySettings;
    private readonly string mapHostDirectory;
    private LocalMapHostServer? localServer;
    private bool allowClose;
    private bool initialized;
    private string? pendingUrl;
    private MapLayerSelection? appliedSelection;
    private ulong activeNavigationId;
    private bool waitingForPage;

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

        Controls.Add(browser);
        Controls.Add(statusLabel);
        Load += (_, _) => SetInitialBounds();
        Shown += (_, _) => InitializeBrowser();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
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

        pendingUrl = selection.Url;
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
            browser.CoreWebView2.Navigate(BuildLocalUrl(pendingUrl));
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
            initialized = true;
            localServer = LocalMapHostServer.Start(mapHostDirectory);
            browser.CoreWebView2.Navigate(BuildLocalUrl(pendingUrl));
        }
        catch (Exception exception)
        {
            statusLabel.Text = $"地图工具网页初始化失败：{exception.Message}";
        }
    }

    private string BuildLocalUrl(string? selectedUrl)
    {
        string query = selectedUrl is null
            ? string.Empty
            : new Uri(selectedUrl).Query;
        return $"{localServer!.BaseUrl}index.html{query}";
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        activeNavigationId = e.NavigationId;
        waitingForPage = !string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.NavigationId != activeNavigationId || !waitingForPage)
        {
            return;
        }

        if (e.IsSuccess)
        {
            return;
        }

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
