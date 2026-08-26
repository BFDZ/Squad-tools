using System.Windows.Forms;

namespace SquadTools;

internal static class LegacyRuntime
{
    private static readonly ManualResetEventSlim Ready = new(false);
    private static Thread? thread;
    private static MainForm? form;
    private static int stopped;

    internal static void Start(bool webView2Available)
    {
        stopped = 0;
        thread = new Thread(() =>
        {
            ApplicationConfiguration.Initialize();
            form = new MainForm(webView2Available, true);
            form.Hide();
            Ready.Set();
            Application.Run(form);
        })
        {
            IsBackground = true,
            Name = "SquadTools Win32 Services"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ready.Wait();
    }

    internal static void ToggleBuild() => Invoke(() => form!.ToggleBuildFromShell());
    internal static void ToggleRapidPaste() => Invoke(() => form!.ToggleRapidPasteFromShell());
    internal static void ToggleAutoRun() => Invoke(() => form!.ToggleAutoRunFromShell());
    internal static void ConfigureRapidPaste(string squadName, int intervalMilliseconds) =>
        Invoke(() => form!.ConfigureRapidPasteFromShell(squadName, intervalMilliseconds));
    internal static void ShowMapTool() => Invoke(() => form!.ShowMapToolFromShell());

    internal static void ShowMainWindow() => MainWindow.Current?.ShowFromTray();

    internal static void ExitApplication() => MainWindow.Current?.CloseFromTray();

    internal static void Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0 || form is null) return;
        Invoke(() => form!.ShutdownFromShell());
        thread?.Join(TimeSpan.FromSeconds(3));
        Ready.Dispose();
    }

    private static void Invoke(Action action)
    {
        MainForm? current = form;
        if (current is null || current.IsDisposed) return;
        current.BeginInvoke(action);
    }
}
