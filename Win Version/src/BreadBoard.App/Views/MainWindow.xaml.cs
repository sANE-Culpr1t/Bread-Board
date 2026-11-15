using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.Navigation;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Views;

public partial class MainWindow : Window
{
    private static readonly (PageId Id, string Label)[] Pages =
    [
        (PageId.Soundboard, "Soundboard"),
        (PageId.WebPlayer, "Web Player"),
        (PageId.Mixer, "Mixer"),
        (PageId.Shortcuts, "Shortcuts"),
        (PageId.Settings, "Settings"),
    ];

    private readonly AppHost _host;
    private readonly Func<int, Task> _exit;
    private readonly Dictionary<PageId, RadioButton> _navButtons = [];
    private bool _closing;

    public MainWindow(AppHost host, Func<int, Task> exit)
    {
        _host = host;
        _exit = exit;
        InitializeComponent();
        BuildInfo.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · foundation build";
        RestorePlacement();

        foreach (var (id, label) in Pages)
        {
            var b = new RadioButton { Content = label, Style = (Style)FindResource("NavButton"), GroupName = "nav", Tag = id };
            b.Checked += (_, _) => host.Navigation.Navigate(id);
            _navButtons[id] = b;
            Nav.Children.Add(b);
        }

        host.Navigation.SetFactory(id => new PlaceholderPage(Pages.First(p => p.Id == id).Label, id));
        host.Navigation.Navigated += (id, view) =>
        {
            PageHost.Content = view;
            _navButtons[id].IsChecked = true;
        };
        host.Navigation.Navigate(host.Navigation.Current);

        SourceInitialized += (_, _) => UseDarkTitleBar();
        ContentRendered += (_, _) => Log.Info($"Main window rendered after {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms");
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing) return;
        e.Cancel = true; // finish shutting down services first, then exit
        _closing = true;
        SavePlacement();
        await _exit(0);
    }

    private void RestorePlacement()
    {
        var ui = _host.Config.Current.Ui;
        Width = Math.Max(MinWidth, ui.WindowWidth);
        Height = Math.Max(MinHeight, ui.WindowHeight);
        if (ui.WindowLeft is { } l && ui.WindowTop is { } t &&
            l >= SystemParameters.VirtualScreenLeft - 50 && t >= SystemParameters.VirtualScreenTop - 50 &&
            l + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            t + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (ui.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _host.Config.Update(c =>
        {
            c.Ui.WindowLeft = r.Left;
            c.Ui.WindowTop = r.Top;
            c.Ui.WindowWidth = r.Width;
            c.Ui.WindowHeight = r.Height;
            c.Ui.WindowMaximized = WindowState == WindowState.Maximized;
        });
    }

    private void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        _ = DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
