using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SquadTools;

internal sealed class MainForm : Form
{
    private const int F9HotKeyId = 1;
    private const int F10HotKeyId = 2;
    private const int F8HotKeyId = 3;

    private readonly BuildAssistController buildAssist = new();
    private readonly RapidPasteService rapidPaste = new();
    private readonly AutoRunController autoRun = new();
    private readonly SquadLogReader squadLogReader;
    private readonly MapGuessForm mapGuessForm;
    private readonly ProxySettings proxySettings;
    private readonly bool webView2Available;
    private readonly CheckBox buildSwitch = new();
    private readonly CheckBox rapidPasteSwitch = new();
    private readonly CheckBox autoRunSwitch = new();
    private readonly Label buildStatus = new();
    private readonly Label rapidPasteStatus = new();
    private readonly Label autoRunStatus = new();
    private readonly Label mapGuessStatus = new();
    private readonly CheckBox proxySwitch = new();
    private readonly ComboBox proxyTypeBox = new();
    private readonly TextBox proxyHostBox = new();
    private readonly NumericUpDown proxyPortBox = new();
    private readonly Label proxyStatus = new();
    private readonly TextBox squadNameBox = new();
    private readonly NotifyIcon trayIcon;
    private readonly Icon applicationIcon;
    private int pasteIntervalMilliseconds = 50;
    private bool allowClose;

    internal MainForm(bool webView2Available)
    {
        this.webView2Available = webView2Available;
        Text = "Squad小帮手";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 430);
        MinimumSize = new Size(560, 430);
        MaximumSize = new Size(560, 430);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        Icon = applicationIcon;
        proxySettings = ProxySettingsStore.Load();
        squadLogReader = new SquadLogReader();
        string mapHostDirectory = Path.Combine(Application.StartupPath, "MapHost");
        mapGuessForm = new MapGuessForm(applicationIcon, proxySettings, mapHostDirectory);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Point(16, 7)
        };
        tabs.TabPages.Add(CreateBuildPage());
        tabs.TabPages.Add(CreateRapidPastePage());
        tabs.TabPages.Add(CreateAutoRunPage());
        tabs.TabPages.Add(CreateMapToolPage());
        tabs.TabPages.Add(CreateSettingsPage());

        Label footer = new()
        {
            Dock = DockStyle.Fill,
             Text = "作者: lyl-103  版本号: 1.4.2",
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 12, 0),
            ForeColor = Color.FromArgb(105, 110, 115),
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point)
        };
        TableLayoutPanel shell = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        shell.Controls.Add(tabs, 0, 0);
        shell.Controls.Add(footer, 0, 1);
        Controls.Add(shell);

        ContextMenuStrip trayMenu = new();
        trayMenu.Items.Add("显示主界面", null, (_, _) => ShowMainWindow());
        ToolStripMenuItem mapToolMenuItem = new("地图工具")
        {
            Enabled = webView2Available
        };
        mapToolMenuItem.Click += (_, _) => mapGuessForm.ShowWindow();
        trayMenu.Items.Add(mapToolMenuItem);
        trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        trayIcon = new NotifyIcon
        {
            Icon = (Icon)applicationIcon.Clone(),
            Text = "Squad小帮手",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        buildAssist.StatusChanged += message => UpdateControl(buildStatus, $"当前状态：{message}（F8 切换）");
        buildAssist.Error += message => ShowBuildError(message);
        rapidPaste.Error += ShowRapidPasteError;
        autoRun.StatusChanged += message => UpdateControl(autoRunStatus, $"当前状态：{message}（F10 切换）");
        autoRun.Error += ShowAutoRunError;
        autoRun.Stopped += SynchronizeStoppedAutoRun;
        squadLogReader.MapLayerChanged += selection =>
        {
            mapGuessForm.ApplyMapLayer(selection);
            UpdateControl(mapGuessStatus, $"当前地图：{selection.Map}    Layer：{selection.Layer}");
        };
        squadLogReader.StatusChanged += message => UpdateControl(mapGuessStatus, message);
        squadLogReader.ScanNow();
        PrepareClipboard();

        FormClosing += OnFormClosing;
        FormClosed += (_, _) => DisposeServices();
    }

    private TabPage CreateBuildPage()
    {
        TabPage page = new("自动铲子") { BackColor = Color.FromArgb(250, 250, 250) };
        ConfigureToggle(buildSwitch, "自动铲子：关闭", new Point(28, 30));
        buildSwitch.CheckedChanged += (_, _) => SetBuildAssist(buildSwitch.Checked);

        buildStatus.AutoSize = false;
        buildStatus.Text = "当前状态：未启用（F8 切换）";
        buildStatus.Location = new Point(28, 106);
        buildStatus.Size = new Size(480, 30);
        buildStatus.ForeColor = Color.FromArgb(55, 60, 65);

        Label description = new()
        {
            AutoSize = false,
            Text = "长按左键，建造工事；长按右键，刨除工事",
            Location = new Point(28, 160),
            Size = new Size(480, 48),
            ForeColor = Color.FromArgb(85, 90, 95)
        };

        page.Controls.Add(buildSwitch);
        page.Controls.Add(buildStatus);
        page.Controls.Add(description);
        return page;
    }

    private TabPage CreateRapidPastePage()
    {
        TabPage page = new("极速抢车") { BackColor = Color.FromArgb(250, 250, 250) };

        Label nameLabel = new() { Text = "小队名称", AutoSize = true, Location = new Point(28, 38) };
        squadNameBox.Text = "TANK";
        squadNameBox.Location = new Point(124, 32);
        squadNameBox.Size = new Size(200, 29);
        squadNameBox.TextChanged += (_, _) => PrepareClipboard();

        Label intervalLabel = new() { Text = "发送间隔", AutoSize = true, Location = new Point(28, 91) };
        FlowLayoutPanel intervalPanel = new()
        {
            Location = new Point(124, 82),
            Size = new Size(385, 38),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        intervalPanel.Controls.Add(CreateIntervalOption("超快 10ms", 10, false));
        intervalPanel.Controls.Add(CreateIntervalOption("快速 50ms", 50, true));
        intervalPanel.Controls.Add(CreateIntervalOption("标准 100ms", 100, false));

        ConfigureToggle(rapidPasteSwitch, "极速抢车：关闭", new Point(28, 142));
        rapidPasteSwitch.CheckedChanged += (_, _) => SetRapidPaste(rapidPasteSwitch.Checked);

        rapidPasteStatus.AutoSize = false;
        rapidPasteStatus.Text = "当前状态：已停止（F9 切换）";
        rapidPasteStatus.Location = new Point(28, 218);
        rapidPasteStatus.Size = new Size(480, 30);
        rapidPasteStatus.ForeColor = Color.FromArgb(55, 60, 65);

        Label modeDescription = new()
        {
            AutoSize = false,
            Text = "仅在 Squad 位于前台时循环粘贴建队命令",
            Location = new Point(28, 270),
            Size = new Size(480, 40),
            ForeColor = Color.FromArgb(85, 90, 95)
        };

        page.Controls.Add(nameLabel);
        page.Controls.Add(squadNameBox);
        page.Controls.Add(intervalLabel);
        page.Controls.Add(intervalPanel);
        page.Controls.Add(rapidPasteSwitch);
        page.Controls.Add(rapidPasteStatus);
        page.Controls.Add(modeDescription);
        return page;
    }

    private TabPage CreateAutoRunPage()
    {
        TabPage page = new("自动奔跑") { BackColor = Color.FromArgb(250, 250, 250) };
        ConfigureToggle(autoRunSwitch, "自动奔跑：关闭", new Point(28, 30));
        autoRunSwitch.CheckedChanged += (_, _) => SetAutoRun(autoRunSwitch.Checked);

        autoRunStatus.AutoSize = false;
        autoRunStatus.Text = "当前状态：未启用（F10 切换）";
        autoRunStatus.Location = new Point(28, 106);
        autoRunStatus.Size = new Size(480, 30);
        autoRunStatus.ForeColor = Color.FromArgb(55, 60, 65);

        Label description = new()
        {
            AutoSize = false,
            Text = "适用于无限体力的服务器，开启后自动奔跑",
            Location = new Point(28, 160),
            Size = new Size(480, 48),
            ForeColor = Color.FromArgb(85, 90, 95)
        };

        page.Controls.Add(autoRunSwitch);
        page.Controls.Add(autoRunStatus);
        page.Controls.Add(description);
        return page;
    }

    private TabPage CreateMapToolPage()
    {
        TabPage page = new("地图工具") { BackColor = Color.FromArgb(250, 250, 250) };
        Button openButton = new()
        {
            Text = "打开地图工具窗口",
            Location = new Point(28, 30),
            Size = new Size(220, 48),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(236, 239, 241),
            ForeColor = Color.FromArgb(32, 37, 41),
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
        };
        openButton.Enabled = webView2Available;
        openButton.Click += (_, _) => mapGuessForm.ShowWindow();

        mapGuessStatus.AutoSize = false;
        mapGuessStatus.Text = webView2Available
            ? "等待 Squad 地图日志"
            : "未安装 WebView2 Runtime，地图工具不可用";
        mapGuessStatus.Location = new Point(28, 106);
        mapGuessStatus.Size = new Size(480, 30);
        mapGuessStatus.ForeColor = Color.FromArgb(55, 60, 65);

        Label description = new()
        {
            AutoSize = false,
            Text = "支持地图点位刷新预测，以及迫击炮等远程火力坐标计算",
            Location = new Point(28, 160),
            Size = new Size(480, 48),
            ForeColor = Color.FromArgb(85, 90, 95)
        };

        page.Controls.Add(openButton);
        page.Controls.Add(mapGuessStatus);
        page.Controls.Add(description);
        return page;
    }

    private TabPage CreateSettingsPage()
    {
        TabPage page = new("设置") { BackColor = Color.FromArgb(250, 250, 250) };

        proxySwitch.Appearance = Appearance.Button;
        proxySwitch.AutoSize = false;
        proxySwitch.Text = proxySettings.Enabled ? "网络代理：开启" : "网络代理：关闭";
        proxySwitch.TextAlign = ContentAlignment.MiddleCenter;
        proxySwitch.Size = new Size(220, 48);
        proxySwitch.Location = new Point(28, 30);
        proxySwitch.BackColor = proxySettings.Enabled
            ? Color.FromArgb(210, 241, 224)
            : Color.FromArgb(236, 239, 241);
        proxySwitch.FlatStyle = FlatStyle.Flat;
        proxySwitch.FlatAppearance.BorderSize = 1;
        proxySwitch.FlatAppearance.BorderColor = proxySettings.Enabled
            ? Color.FromArgb(91, 162, 119)
            : Color.FromArgb(174, 181, 187);
        proxySwitch.Checked = proxySettings.Enabled;
        proxySwitch.CheckedChanged += (_, _) =>
        {
            proxySwitch.Text = proxySwitch.Checked ? "网络代理：开启" : "网络代理：关闭";
            proxySwitch.BackColor = proxySwitch.Checked
                ? Color.FromArgb(210, 241, 224)
                : Color.FromArgb(236, 239, 241);
            proxySwitch.FlatAppearance.BorderColor = proxySwitch.Checked
                ? Color.FromArgb(91, 162, 119)
                : Color.FromArgb(174, 181, 187);
        };

        Label typeLabel = new() { Text = "代理类型", AutoSize = true, Location = new Point(28, 108) };
        proxyTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        proxyTypeBox.Items.AddRange(["HTTP", "SOCKS5"]);
        proxyTypeBox.SelectedIndex = proxySettings.Type == ProxyType.Socks5 ? 1 : 0;
        proxyTypeBox.Location = new Point(124, 102);
        proxyTypeBox.Size = new Size(140, 29);

        Label hostLabel = new() { Text = "IP 地址", AutoSize = true, Location = new Point(28, 155) };
        proxyHostBox.Text = proxySettings.Host;
        proxyHostBox.PlaceholderText = "例如 127.0.0.1";
        proxyHostBox.Location = new Point(124, 149);
        proxyHostBox.Size = new Size(220, 29);

        Label portLabel = new() { Text = "端口", AutoSize = true, Location = new Point(28, 202) };
        proxyPortBox.Minimum = 1;
        proxyPortBox.Maximum = 65535;
        proxyPortBox.Value = Math.Clamp(proxySettings.Port, 1, 65535);
        proxyPortBox.Location = new Point(124, 196);
        proxyPortBox.Size = new Size(140, 29);

        Button saveButton = new()
        {
            Text = "保存代理设置",
            Location = new Point(28, 250),
            Size = new Size(180, 42),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(236, 239, 241),
            ForeColor = Color.FromArgb(32, 37, 41)
        };
        saveButton.Click += (_, _) => SaveProxySettings();

        proxyStatus.AutoSize = false;
        proxyStatus.Location = new Point(28, 315);
        proxyStatus.Size = new Size(480, 48);
        proxyStatus.ForeColor = Color.FromArgb(55, 60, 65);
        proxyStatus.Text = "代理设置将在首次创建地图工具网页时生效；已打开过网页时请重启程序。";

        page.Controls.Add(proxySwitch);
        page.Controls.Add(typeLabel);
        page.Controls.Add(proxyTypeBox);
        page.Controls.Add(hostLabel);
        page.Controls.Add(proxyHostBox);
        page.Controls.Add(portLabel);
        page.Controls.Add(proxyPortBox);
        page.Controls.Add(saveButton);
        page.Controls.Add(proxyStatus);
        return page;
    }

    private void SaveProxySettings()
    {
        string host = proxyHostBox.Text.Trim();
        int port = (int)proxyPortBox.Value;
        if (proxySwitch.Checked && host.Length == 0)
        {
            proxyStatus.Text = "启用代理时必须填写 IP 地址或主机名。";
            return;
        }

        proxySettings.Enabled = proxySwitch.Checked;
        proxySettings.Type = proxyTypeBox.SelectedIndex == 1 ? ProxyType.Socks5 : ProxyType.Http;
        proxySettings.Host = host;
        proxySettings.Port = port;
        if (!ProxySettingsStore.Save(proxySettings, out string error))
        {
            proxyStatus.Text = error;
            return;
        }

        proxyStatus.Text = proxySettings.Enabled
            ? $"代理设置已保存：{proxySettings.Type} {proxySettings.Host}:{proxySettings.Port}，首次创建网页时生效。"
            : "网络代理已关闭，设置已保存。";
    }

    private static void ConfigureToggle(CheckBox toggle, string text, Point location)
    {
        toggle.Appearance = Appearance.Button;
        toggle.AutoSize = false;
        toggle.Text = text;
        toggle.TextAlign = ContentAlignment.MiddleCenter;
        toggle.Size = new Size(220, 48);
        toggle.Location = location;
        toggle.BackColor = Color.FromArgb(236, 239, 241);
        toggle.ForeColor = Color.FromArgb(32, 37, 41);
        toggle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        toggle.FlatStyle = FlatStyle.Flat;
        toggle.FlatAppearance.BorderSize = 1;
        toggle.FlatAppearance.BorderColor = Color.FromArgb(174, 181, 187);
        toggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(225, 230, 234);
        toggle.FlatAppearance.CheckedBackColor = Color.FromArgb(210, 241, 224);
    }

    private RadioButton CreateIntervalOption(string text, int milliseconds, bool isChecked)
    {
        RadioButton option = new()
        {
            AutoSize = true,
            Text = text,
            Checked = isChecked,
            Margin = new Padding(0, 4, 14, 0)
        };
        option.CheckedChanged += (_, _) =>
        {
            if (!option.Checked)
            {
                return;
            }

            pasteIntervalMilliseconds = milliseconds;
            if (rapidPaste.IsRunning)
            {
                rapidPaste.Start(pasteIntervalMilliseconds);
                UpdateRapidPasteStatus();
            }
        };
        return option;
    }

    private void SetBuildAssist(bool enabled)
    {
        buildAssist.SetEnabled(enabled);
        buildSwitch.Text = enabled ? "自动铲子：开启" : "自动铲子：关闭";
        buildSwitch.BackColor = enabled ? Color.FromArgb(210, 241, 224) : Color.FromArgb(236, 239, 241);
        buildSwitch.FlatAppearance.BorderColor = enabled ? Color.FromArgb(91, 162, 119) : Color.FromArgb(174, 181, 187);
    }

    private void SetRapidPaste(bool enabled)
    {
        if (!enabled)
        {
            rapidPaste.Stop();
            rapidPasteSwitch.Text = "极速抢车：关闭";
            rapidPasteSwitch.BackColor = Color.FromArgb(236, 239, 241);
            rapidPasteSwitch.FlatAppearance.BorderColor = Color.FromArgb(174, 181, 187);
            rapidPasteStatus.Text = "当前状态：已停止（F9 切换）";
            return;
        }

        string squadName = squadNameBox.Text.Trim();
        if (squadName.Length == 0)
        {
            rapidPasteSwitch.Checked = false;
            MessageBox.Show(this, "请输入有效的小队名称。", "极速抢车", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!PrepareClipboard())
        {
            rapidPasteSwitch.Checked = false;
            MessageBox.Show(this, "无法访问剪贴板。", "极速抢车", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        rapidPaste.Start(pasteIntervalMilliseconds);
        rapidPasteSwitch.Text = "极速抢车：开启";
        rapidPasteSwitch.BackColor = Color.FromArgb(210, 241, 224);
        rapidPasteSwitch.FlatAppearance.BorderColor = Color.FromArgb(91, 162, 119);
        UpdateRapidPasteStatus();
    }

    private void SetAutoRun(bool enabled)
    {
        autoRun.SetEnabled(enabled);
        bool active = autoRun.Enabled;
        autoRunSwitch.Text = active ? "自动奔跑：开启" : "自动奔跑：关闭";
        autoRunSwitch.BackColor = active ? Color.FromArgb(210, 241, 224) : Color.FromArgb(236, 239, 241);
        autoRunSwitch.FlatAppearance.BorderColor = active
            ? Color.FromArgb(91, 162, 119)
            : Color.FromArgb(174, 181, 187);
    }

    private bool PrepareClipboard()
    {
        string squadName = squadNameBox.Text.Trim();
        if (squadName.Length == 0)
        {
            return false;
        }

        try
        {
            Clipboard.SetText($"createsquad {squadName} 1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateRapidPasteStatus()
    {
        rapidPasteStatus.Text = $"当前状态：运行中，{squadNameBox.Text.Trim()}，间隔 {pasteIntervalMilliseconds}ms";
    }

    private void UpdateControl(Control control, string text)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateControl(control, text));
            return;
        }

        control.Text = text;
    }

    private void ShowBuildError(string message)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (buildSwitch.Checked)
            {
                buildSwitch.Checked = false;
            }

            MessageBox.Show(this, message, "自动铲子", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        });
    }

    private void ShowRapidPasteError(string message)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (rapidPasteSwitch.Checked)
            {
                rapidPasteSwitch.Checked = false;
            }

            MessageBox.Show(this, message, "极速抢车", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        });
    }

    private void ShowAutoRunError(string message)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (autoRunSwitch.Checked)
            {
                autoRunSwitch.Checked = false;
            }

            MessageBox.Show(this, message, "自动奔跑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        });
    }

    private void SynchronizeStoppedAutoRun(string status)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (autoRunSwitch.Checked)
            {
                autoRunSwitch.Checked = false;
            }

            autoRunStatus.Text = $"当前状态：{status}（F10 切换）";
        });
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotKey && message.WParam.ToInt32() == F8HotKeyId)
        {
            buildSwitch.Checked = !buildSwitch.Checked;
        }

        if (message.Msg == NativeMethods.WmHotKey && message.WParam.ToInt32() == F9HotKeyId)
        {
            rapidPasteSwitch.Checked = !rapidPasteSwitch.Checked;
        }

        if (message.Msg == NativeMethods.WmHotKey && message.WParam.ToInt32() == F10HotKeyId)
        {
            autoRunSwitch.Checked = !autoRunSwitch.Checked;
        }

        base.WndProc(ref message);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!NativeMethods.RegisterHotKey(Handle, F9HotKeyId, 0, NativeMethods.VkF9))
        {
            int error = Marshal.GetLastWin32Error();
            rapidPasteStatus.Text = $"当前状态：F9 热键注册失败（Win32 {error}），可能已被占用";
        }

        if (!NativeMethods.RegisterHotKey(Handle, F8HotKeyId, 0, NativeMethods.VkF8))
        {
            int error = Marshal.GetLastWin32Error();
            buildStatus.Text = $"当前状态：F8 热键注册失败（Win32 {error}），可能已被占用";
        }

        if (!NativeMethods.RegisterHotKey(Handle, F10HotKeyId, 0, NativeMethods.VkF10))
        {
            int error = Marshal.GetLastWin32Error();
            autoRunStatus.Text = $"当前状态：F10 热键注册失败（Win32 {error}），可能已被占用";
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(Handle, F8HotKeyId);
            NativeMethods.UnregisterHotKey(Handle, F9HotKeyId);
            NativeMethods.UnregisterHotKey(Handle, F10HotKeyId);
        }

        base.OnHandleDestroyed(e);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        allowClose = true;
        Close();
    }

    private void DisposeServices()
    {
        mapGuessForm.CloseWindow();
        squadLogReader.Dispose();
        rapidPaste.Dispose();
        autoRun.Dispose();
        buildAssist.Dispose();
        trayIcon.Visible = false;
        trayIcon.Icon?.Dispose();
        trayIcon.Dispose();
        applicationIcon.Dispose();
    }
}
