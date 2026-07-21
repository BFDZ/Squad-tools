using System;
using System.Drawing;
using System.Windows.Forms;

namespace SquadTools;

internal sealed class MainForm : Form
{
    private const int F9HotKeyId = 1;

    private readonly BuildAssistController buildAssist = new();
    private readonly RapidPasteService rapidPaste = new();
    private readonly CheckBox buildSwitch = new();
    private readonly CheckBox rapidPasteSwitch = new();
    private readonly Label buildStatus = new();
    private readonly Label rapidPasteStatus = new();
    private readonly TextBox squadNameBox = new();
    private readonly NotifyIcon trayIcon;
    private int pasteIntervalMilliseconds = 50;
    private bool allowClose;

    internal MainForm()
    {
        Text = "Squad 工具";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 430);
        MinimumSize = new Size(560, 430);
        MaximumSize = new Size(560, 430);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateBuildPage());
        tabs.TabPages.Add(CreateRapidPastePage());
        Controls.Add(tabs);

        ContextMenuStrip trayMenu = new();
        trayMenu.Items.Add("显示主界面", null, (_, _) => ShowMainWindow());
        trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Squad 工具",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        buildAssist.StatusChanged += message => UpdateControl(buildStatus, $"当前状态：{message}");
        buildAssist.Error += message => ShowBuildError(message);
        rapidPaste.Error += ShowRapidPasteError;
        PrepareClipboard();

        FormClosing += OnFormClosing;
        FormClosed += (_, _) => DisposeServices();
    }

    private TabPage CreateBuildPage()
    {
        TabPage page = new("自动铲子") { Padding = new Padding(24) };
        buildSwitch.Appearance = Appearance.Button;
        buildSwitch.AutoSize = false;
        buildSwitch.Text = "自动铲子：关闭";
        buildSwitch.TextAlign = ContentAlignment.MiddleCenter;
        buildSwitch.Size = new Size(210, 48);
        buildSwitch.BackColor = Color.FromArgb(242, 242, 242);
        buildSwitch.FlatStyle = FlatStyle.Flat;
        buildSwitch.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
        buildSwitch.CheckedChanged += (_, _) => SetBuildAssist(buildSwitch.Checked);

        buildStatus.AutoSize = false;
        buildStatus.Text = "当前状态：未启用";
        buildStatus.Location = new Point(0, 78);
        buildStatus.Size = new Size(480, 30);

        Label description = new()
        {
            AutoSize = false,
            Text = "长按左键，建造工事；长按右键，刨除工事",
            Location = new Point(0, 120),
            Size = new Size(480, 48)
        };

        page.Controls.Add(buildSwitch);
        page.Controls.Add(buildStatus);
        page.Controls.Add(description);
        return page;
    }

    private TabPage CreateRapidPastePage()
    {
        TabPage page = new("极速抢车") { Padding = new Padding(24) };

        Label nameLabel = new() { Text = "小队名称", AutoSize = true, Location = new Point(0, 8) };
        squadNameBox.Text = "TANK";
        squadNameBox.Location = new Point(95, 4);
        squadNameBox.Size = new Size(180, 28);
        squadNameBox.TextChanged += (_, _) => PrepareClipboard();

        Label intervalLabel = new() { Text = "发送间隔", AutoSize = true, Location = new Point(0, 54) };
        FlowLayoutPanel intervalPanel = new()
        {
            Location = new Point(95, 46),
            Size = new Size(385, 35),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        intervalPanel.Controls.Add(CreateIntervalOption("超快 10ms", 10, false));
        intervalPanel.Controls.Add(CreateIntervalOption("快速 50ms", 50, true));
        intervalPanel.Controls.Add(CreateIntervalOption("标准 100ms", 100, false));

        rapidPasteSwitch.Appearance = Appearance.Button;
        rapidPasteSwitch.AutoSize = false;
        rapidPasteSwitch.Text = "极速抢车：关闭";
        rapidPasteSwitch.TextAlign = ContentAlignment.MiddleCenter;
        rapidPasteSwitch.Size = new Size(210, 48);
        rapidPasteSwitch.Location = new Point(0, 100);
        rapidPasteSwitch.BackColor = Color.FromArgb(242, 242, 242);
        rapidPasteSwitch.FlatStyle = FlatStyle.Flat;
        rapidPasteSwitch.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
        rapidPasteSwitch.CheckedChanged += (_, _) => SetRapidPaste(rapidPasteSwitch.Checked);

        rapidPasteStatus.AutoSize = false;
        rapidPasteStatus.Text = "当前状态：已停止（F9 切换）";
        rapidPasteStatus.Location = new Point(0, 168);
        rapidPasteStatus.Size = new Size(480, 30);

        Label modeDescription = new()
        {
            AutoSize = false,
            Text = "仅在 Squad 位于前台时循环粘贴建队命令",
            Location = new Point(0, 208),
            Size = new Size(480, 40)
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
        buildSwitch.BackColor = enabled ? Color.FromArgb(213, 245, 227) : Color.FromArgb(242, 242, 242);
    }

    private void SetRapidPaste(bool enabled)
    {
        if (!enabled)
        {
            rapidPaste.Stop();
            rapidPasteSwitch.Text = "极速抢车：关闭";
            rapidPasteSwitch.BackColor = Color.FromArgb(242, 242, 242);
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
        rapidPasteSwitch.BackColor = Color.FromArgb(213, 245, 227);
        UpdateRapidPasteStatus();
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

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotKey && message.WParam.ToInt32() == F9HotKeyId)
        {
            rapidPasteSwitch.Checked = !rapidPasteSwitch.Checked;
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
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(Handle, F9HotKeyId);
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
        buildAssist.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }
}
