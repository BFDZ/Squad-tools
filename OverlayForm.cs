using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SquadTools;

internal sealed class OverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private readonly Label messageLabel;

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

    internal OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(180, 54);
        BackColor = Color.FromArgb(28, 30, 33);
        Opacity = 0.86;

        messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point)
        };
        Controls.Add(messageLabel);
    }

    internal void ShowStatus(string message)
    {
        messageLabel.Text = message;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 54 - (int)(area.Height * 0.27));

        if (!Visible)
        {
            Show();
        }
    }
}
