using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SquadTools;

internal sealed class MapGuessForm : Form
{
    private const string HomeUrl = "https://squadcalc.app/";
    private readonly WebView2 browser = new();
    private readonly Label statusLabel = new();
    private readonly Panel loadingPanel = new();
    private readonly Label loadingLabel = new();
    private readonly Icon applicationIcon;
    private readonly ProxySettings proxySettings;
    private readonly string mapHostDirectory;
    private LocalMapHostServer? localServer;
    private bool allowClose;
    private bool initialized;
    private string? pendingUrl;
    private ulong activeNavigationId;
    private bool waitingForPage;

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

        loadingLabel.AutoSize = true;
        loadingLabel.Text = "加载中...";
        loadingLabel.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Regular, GraphicsUnit.Point);
        loadingLabel.ForeColor = Color.FromArgb(75, 80, 85);

        loadingPanel.Dock = DockStyle.Fill;
        loadingPanel.BackColor = Color.FromArgb(245, 245, 245);
        loadingPanel.Controls.Add(loadingLabel);
        loadingPanel.Resize += (_, _) => CenterLoadingLabel();

        Controls.Add(browser);
        Controls.Add(loadingPanel);
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
        statusLabel.Text = $"当前地图：{selection.Map}    Layer：{selection.Layer}";
        if (initialized && browser.CoreWebView2 is not null)
        {
            string localUrl = BuildLocalUrl(pendingUrl);
            if (string.Equals(browser.Source?.ToString(), localUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            browser.CoreWebView2.Navigate(localUrl);
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
            loadingLabel.Text = "网页加载失败";
            CenterLoadingLabel();
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
        if (!waitingForPage)
        {
            return;
        }

        loadingLabel.Text = "加载中...";
        CenterLoadingLabel();
        loadingPanel.Visible = true;
        loadingPanel.BringToFront();
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.NavigationId != activeNavigationId || !waitingForPage)
        {
            return;
        }

        if (e.IsSuccess)
        {
            loadingPanel.Visible = false;
            loadingPanel.SendToBack();
            return;
        }

        loadingLabel.Text = "网页加载失败";
        CenterLoadingLabel();
        statusLabel.Text = $"地图工具网页加载失败：{e.WebErrorStatus}";
    }

    private void CenterLoadingLabel()
    {
        loadingLabel.Location = new Point(
            Math.Max(0, (loadingPanel.ClientSize.Width - loadingLabel.Width) / 2),
            Math.Max(0, (loadingPanel.ClientSize.Height - loadingLabel.Height) / 2));
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
