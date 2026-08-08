using System;
using System.Drawing;
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
    private readonly CheckBox buildSwitch = new();
    private readonly CheckBox rapidPasteSwitch = new();
    private readonly CheckBox autoRunSwitch = new();
    private readonly Label buildStatus = new();
    private readonly Label rapidPasteStatus = new();
    private readonly Label autoRunStatus = new();
    private readonly TextBox squadNameBox = new();
    private readonly NotifyIcon trayIcon;
    private readonly Icon applicationIcon;
    private int pasteIntervalMilliseconds = 50;
    private bool allowClose;

    internal MainForm()
    {
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

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Point(16, 7)
        };
        tabs.TabPages.Add(CreateBuildPage());
        tabs.TabPages.Add(CreateRapidPastePage());
        tabs.TabPages.Add(CreateAutoRunPage());

        Label footer = new()
        {
            Dock = DockStyle.Fill,
            Text = "作者: lyl-103  版本号: 1.4.0",
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
            rapidPasteStatus.Text = "当前状态：F9 热键注册失败，可能已被占用";
        }

        if (!NativeMethods.RegisterHotKey(Handle, F8HotKeyId, 0, NativeMethods.VkF8))
        {
            buildStatus.Text = "当前状态：F8 热键注册失败，可能已被占用";
        }

        if (!NativeMethods.RegisterHotKey(Handle, F10HotKeyId, 0, NativeMethods.VkF10))
        {
            autoRunStatus.Text = "当前状态：F10 热键注册失败，可能已被占用";
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
        rapidPaste.Dispose();
        autoRun.Dispose();
        buildAssist.Dispose();
        trayIcon.Visible = false;
        trayIcon.Icon?.Dispose();
        trayIcon.Dispose();
        applicationIcon.Dispose();
    }
}
