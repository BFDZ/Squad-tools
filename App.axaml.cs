using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SquadTools;

internal sealed class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool webView2Available = desktop.Args is { Length: > 0 } &&
                bool.TryParse(desktop.Args[0], out bool parsed) && parsed;
            desktop.MainWindow = new MainWindow(webView2Available);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
