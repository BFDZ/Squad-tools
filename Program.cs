using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace SquadTools;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        bool webView2Available = CheckWebView2Runtime();
        Application.Run(new MainForm(webView2Available));
    }

    private static bool CheckWebView2Runtime()
    {
        try
        {
            string? version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return true;
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
        }
        catch (COMException)
        {
        }

        DialogResult result = MessageBox.Show(
            "未检测到 Microsoft Edge WebView2 Runtime。地图工具需要安装此运行环境，其他功能仍可正常使用。\n\n是否打开微软官方下载页面？",
            "缺少 WebView2 Runtime",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result == DialogResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
                    UseShellExecute = true
                });
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return false;
    }
}
