using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using SukiUI;
using SukiUI.Controls;
using AvaloniaControl = Avalonia.Controls.Control;
using Button = Avalonia.Controls.Button;
using Panel = Avalonia.Controls.Panel;
using CheckBox = Avalonia.Controls.CheckBox;
using ComboBox = Avalonia.Controls.ComboBox;
using TextBox = Avalonia.Controls.TextBox;
using NumericUpDown = Avalonia.Controls.NumericUpDown;
using Color = Avalonia.Media.Color;

namespace SquadTools;

internal sealed partial class MainWindow : SukiWindow
{
    internal static MainWindow? Current { get; private set; }
    private readonly ProxySettings proxySettings = ProxySettingsStore.Load();
    private int selectedPage;

    internal MainWindow(bool webView2Available)
    {
        Current = this;
        InitializeComponent();
        Width = 1000;
        Height = 800;
        Closed += (_, _) => LegacyRuntime.Stop();
        ShowAutoTools();
        SetNavigationSelection(0);
    }

    private void Navigate(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int page))
        {
            SetNavigationSelection(page);
            selectedPage = page;
            switch (page)
            {
                case 0: ShowAutoTools(); break;
                case 1: ShowMapTools(); break;
                case 2: ShowNetworkSettings(); break;
                default: ShowAbout(); break;
            }
        }
    }

    private void ShowAutoTools()
    {
        PageHost.Content = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                ToolCard("自动铲子", "长按鼠标左键建造，长按右键刨除工事。", "F8", LegacyRuntime.ToggleBuild),
                ToolCard("自动奔跑", "在游戏中自动奔跑，解放双手，适合无限体力服。", "F10", LegacyRuntime.ToggleAutoRun),
                RapidPasteCard()
            }
        };
    }

    private void ShowMapTools()
    {
        PageHost.Content = new StackPanel
        {
            Spacing = 20,
            Children =
            {
                new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(24),
                    Background = new SolidColorBrush(Color.Parse("#142C4F80")),
                    Child = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = "猜点和迫击炮计算器", FontSize = 22, FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = "*支持地图猜点，提前拉点，占领先手位置\n*自动一键计算迫击炮落点，支持多种远程打击武器计算\n*显示热门武器部署位置", Opacity = 0.72, LineHeight = 25 },
                            new Button { Content = "打开地图工具", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Padding = new Thickness(20, 10) }
                        }
                    }
                }
            }
        };
        if (PageHost.Content is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is Border border && border.Child is StackPanel card && card.Children.Count > 2 && card.Children[2] is Button button)
            button.Click += (_, _) => LegacyRuntime.ShowMapTool();
    }

    private void ShowNetworkSettings()
    {
        var enabled = new CheckBox { Content = "启用网络代理", IsChecked = proxySettings.Enabled };
        var type = new ComboBox { Width = 160, SelectedIndex = proxySettings.Type == ProxyType.Socks5 ? 1 : 0, ItemsSource = new[] { "HTTP", "SOCKS5" } };
        var host = new TextBox { Width = 240, Text = proxySettings.Host, Watermark = "IP 地址或主机名" };
        var port = new NumericUpDown { Width = 120, Minimum = 1, Maximum = 65535, Value = proxySettings.Port };
        var status = new TextBlock { Opacity = 0.7 };
        var save = new Button
        {
            Content = "保存设置",
            Width = 120,
            Padding = new Thickness(10, 9),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        save.Click += (_, _) =>
        {
            proxySettings.Enabled = enabled.IsChecked == true;
            proxySettings.Type = type.SelectedIndex == 1 ? ProxyType.Socks5 : ProxyType.Http;
            proxySettings.Host = host.Text?.Trim() ?? string.Empty;
            proxySettings.Port = (int)(port.Value ?? 8080);
            status.Text = proxySettings.Enabled && proxySettings.Host.Length == 0
                ? "启用代理时必须填写地址。"
                : ProxySettingsStore.Save(proxySettings, out string error) ? "已保存，重启后生效。" : error;
        };
        var card = new StackPanel { Spacing = 16 };
        card.Children.Add(new TextBlock { Text = "网络设置", FontSize = 22, FontWeight = FontWeight.SemiBold });
        card.Children.Add(new TextBlock { Text = "地图工具的远程 API 访问使用以下代理。", FontSize = 13, Opacity = 0.65 });
        card.Children.Add(enabled);
        card.Children.Add(SettingRow("代理类型", type));
        card.Children.Add(SettingRow("地址", host));
        card.Children.Add(SettingRow("端口", port));
        card.Children.Add(save);
        card.Children.Add(status);
        PageHost.Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Background = new SolidColorBrush(Color.Parse("#142C4F80")),
            Child = card
        };
    }

    private void SetNavigationSelection(int page)
    {
        foreach (var child in NavigationPanel.Children)
        {
            if (child is Button button)
                button.Classes.Set("Selected", button.Tag is string tag && int.TryParse(tag, out int value) && value == page);
        }
    }

    private void ShowAbout()
    {
        PageHost.Content = new Border
        {
            Width = 640,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(34),
            Background = new SolidColorBrush(Color.Parse("#142C4F80")),
            Child = new StackPanel
            {
                Spacing = 14,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "Squad小帮手", FontSize = 26, FontWeight = FontWeight.SemiBold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "一个 Squad 游戏小工具：本地运行、无需登录，体积小、启动迅速。", FontSize = 14, Opacity = 0.72, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    new TextBlock { Text = "作者：lyl-103", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    LinkButton("发布页：https://github.com/BFDZ/Squad-tools/releases", "https://github.com/BFDZ/Squad-tools/releases"),
                    new Border { Height = 1, Width = 360, Background = new SolidColorBrush(Color.Parse("#50708090")), Margin = new Thickness(0, 6) },
                    new TextBlock { Text = "感谢下列项目", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    LinkButton("tyabase / Squad-Auto-Tool", "https://github.com/tyabase/Squad-Auto-Tool"),
                    LinkButton("SquadCalc.app", "https://squadcalc.app/"),
                    LinkButton("AvaloniaUI / Avalonia", "https://github.com/AvaloniaUI/Avalonia"),
                    LinkButton("Suki UI", "https://github.com/kikipoulet/SukiUI")
                }
            }
        };
    }

    private static TextBlock PageTitle(string title, string subtitle) => new() { Text = $"{title}\n{subtitle}", FontSize = 22, LineHeight = 34 };
    private static Grid SettingRow(string label, AvaloniaControl control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button LinkButton(string text, string url)
    {
        var button = new Button { Content = text, Classes = { "LinkButton" } };
        button.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Opening the system browser is best effort.
            }
        };
        return button;
    }
    private static Border ToolCard(string title, string description, string key, Action action) => new()
    {
        CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Background = new SolidColorBrush(Color.Parse("#142C4F80")),
        Child = CreateToolCardContent(title, description, key, action)
    };

    private static Grid CreateToolCardContent(string title, string description, string key, Action action)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = description, Opacity = 0.7 } } });
        var button = CreateToolSwitch(key, action);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private Border RapidPasteCard()
    {
        var name = new TextBox { Width = 150, Text = "TANK", Watermark = "小队名称" };
        var interval = new ComboBox
        {
            Width = 150,
            SelectedIndex = 1,
            ItemsSource = new[] { "超快 10ms", "快速 50ms", "标准 100ms" }
        };
        void Configure()
        {
            int[] values = [10, 50, 100];
            LegacyRuntime.ConfigureRapidPaste(name.Text?.Trim() ?? string.Empty, values[Math.Max(0, interval.SelectedIndex)]);
        }
        name.TextChanged += (_, _) => Configure();
        interval.SelectionChanged += (_, _) => Configure();

        var toggle = CreateToolSwitch("F9", LegacyRuntime.ToggleRapidPaste);

        var details = new StackPanel { Spacing = 10 };
        details.Children.Add(new TextBlock { Text = "极速抢车", FontSize = 18, FontWeight = FontWeight.SemiBold });
        details.Children.Add(new TextBlock { Text = "在地图加载前，按快捷键开启。", Opacity = 0.7 });
        details.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "小队名称", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, name,
                new TextBlock { Text = "发送间隔", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, interval
            }
        });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 86 };
        grid.Children.Add(details);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(20),
            Background = new SolidColorBrush(Color.Parse("#142C4F80")), Child = grid
        };
    }

    private void ToggleTheme(object? sender, RoutedEventArgs e) => SukiTheme.GetInstance().SwitchBaseTheme();
    private static StackPanel CreateToolSwitch(string key, Action action)
    {
        var toggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            Width = 54,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        toggle.Classes.Add("ToolSwitch");
        toggle.Click += (_, _) => action();
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = { new TextBlock { Text = key, FontSize = 14, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, toggle }
        };
    }
    internal void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }
    internal void CloseFromTray() { Close(); }
}
