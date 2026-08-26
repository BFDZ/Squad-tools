using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Web.WebView2.Core;

namespace SquadTools;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool webView2Available = CheckWebView2Runtime();
        // Keep the WinForms host hidden; it owns the global hotkeys and input services.
        LegacyRuntime.Start(webView2Available);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime([webView2Available.ToString()]);
        LegacyRuntime.Stop();
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

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

        System.Windows.Forms.DialogResult result = System.Windows.Forms.MessageBox.Show(
            "未检测到 Microsoft Edge WebView2 Runtime。地图工具需要安装此运行环境，其他功能仍可正常使用。\n\n是否打开微软官方下载页面？",
            "缺少 WebView2 Runtime",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Warning);
        if (result == System.Windows.Forms.DialogResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
                    UseShellExecute = true
                });
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        return false;
    }
}
