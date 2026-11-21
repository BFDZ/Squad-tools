using System.Drawing;
using System.Windows.Forms;

namespace SquadTools;

internal sealed class TargetOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly Label label;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    internal TargetOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 30, 33);
        Opacity = 0.82;
        Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Point);

        label = new Label
        {
            AutoSize = true,
            Location = new Point(18, 10),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = Font
        };
        Controls.Add(label);
    }

    internal void ShowTargets(IReadOnlyList<string> lines)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => ShowTargets(lines));
            return;
        }

        if (lines.Count == 0)
        {
            Hide();
            return;
        }

        label.Text = string.Join(Environment.NewLine, lines);
        Size textSize = TextRenderer.MeasureText(label.Text, Font);
        int width = Math.Min(textSize.Width + 36, 640);
        int height = textSize.Height + 20;
        Size = new Size(width, height);
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        Location = new Point(area.Left + (area.Width - width) / 2, area.Top + 8);
        if (!Visible)
        {
            Show();
        }

        NativeMethods.KeepWindowTopMost(Handle);
    }

    internal void ClearTargets()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(ClearTargets);
            return;
        }

        Hide();
    }
}
