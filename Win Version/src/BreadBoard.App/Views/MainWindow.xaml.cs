using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.Navigation;
using BreadBoard.App.Theme;
using BreadBoard.App.Views.Pages;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Views;

public partial class MainWindow : Window
{
    private static readonly (PageId Id, string Label, string Glyph)[] Pages =
    [
        (PageId.Soundboard, "Soundboard", Glyph.Grid),
        (PageId.Mixer, "Mixer", Glyph.Mixer),
        (PageId.Shortcuts, "Shortcuts", Glyph.Keyboard),
        (PageId.Settings, "Settings", Glyph.Settings),
    ];
    private const double CompactBelow = 1080;

    private readonly AppHost _host;
    private readonly Func<int, Task> _exit;
    private readonly Dictionary<PageId, RadioButton> _navButtons = [];
    private readonly List<TextBlock> _navLabels = [];
    private MiniPlayerWindow? _mini;
    private bool _closing;

    public MainWindow(AppHost host, Func<int, Task> exit)
    {
        _host = host;
        _exit = exit;
        InitializeComponent();
        Footer.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · Made for gamers ♡";
        RestorePlacement();
        BuildNavigation();

        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();
        StateChanged += (_, _) => OnStateChanged();
        SizeChanged += (_, _) => ApplyCompact(ActualWidth < CompactBelow);

        SearchBox.TextChanged += (_, _) => OnSearch();
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Escape) SearchBox.Clear(); };

        GamingButton.Checked += (_, _) => ShowMini();
        GamingButton.Unchecked += (_, _) => _mini?.Close();

        SourceInitialized += (_, _) => ApplyWindowFrame();
        ContentRendered += async (_, _) =>
        {
            Log.Info($"Main window rendered after {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms");
            if (_host.Config.Current.Ui.MiniOpenOnStart && !host.Options.IsAutomated) GamingButton.IsChecked = true;
            await _host.Soundboard.InitializeAsync();
        };

        var sb = _host.Soundboard;
        sb.Notice += ShowToast;
        sb.EngineChanged += UpdateEngineStatus;
        sb.PlaybackTick += () => BoardMeter.Value = Math.Min(1, sb.SoundboardLevel);
        sb.ToggleMiniRequested = () => { if (_mini is null) ShowMini(); else _mini.Close(); };
        Toast.MouseLeftButtonUp += (_, _) => HideToast();
        _toastTimer.Tick += (_, _) => HideToast();
    }

    private readonly System.Windows.Threading.DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };

    /// <summary>Brief message over the page (errors stay a little longer).</summary>
    public void ShowToast(string text, bool error)
    {
        ToastText.Text = text;
        ToastIcon.Text = error ? "\uE7BA" : "\uE73E";
        ToastIcon.Foreground = (System.Windows.Media.Brush)FindResource(error ? "BadBrush" : "OkBrush");
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        _toastTimer.Interval = TimeSpan.FromSeconds(error ? 7 : 4);
        _toastTimer.Stop();
        _toastTimer.Start();
        Log.Info($"Toast ({(error ? "error" : "info")}): {text}");
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.Visibility = Visibility.Collapsed;
    }

    private void UpdateEngineStatus()
    {
        var sb = _host.Soundboard;
        EngineTitle.Text = sb.EngineOk ? "Soundboard live" : "Audio output problem";
        EngineDetail.Text = sb.EngineSummary;
        EngineDetail.ToolTip = sb.EngineSummary;
        VmDot.Fill = (System.Windows.Media.Brush)FindResource(sb.EngineOk ? "OkBrush" : "BadBrush");
        VmStatus.Text = sb.EngineOk ? "Sending sounds" : "Not connected";
        VmCard.ToolTip = sb.EngineSummary;
    }

    public MiniPlayerWindow? MiniPlayer => _mini;

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    /// <summary>Opens (or focuses) the floating mini-player.</summary>
    public void ShowMini()
    {
        if (_mini is null)
        {
            _mini = new MiniPlayerWindow(_host.Config, _host.Soundboard);
            _mini.Closed += (_, _) => { _mini = null; GamingButton.IsChecked = false; };
            _mini.Show();
        }
        _mini.Activate();
        GamingButton.IsChecked = true;
    }

    private void BuildNavigation()
    {
        foreach (var (id, label, glyph) in Pages)
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            _navLabels.Add(text);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 19, Margin = new Thickness(0, 0, 16, 0) });
            content.Children.Add(text);
            var b = new RadioButton { Content = content, Style = (Style)FindResource("NavButton"), GroupName = "nav", ToolTip = label };
            AutomationPropertiesHelper.SetName(b, label);
            b.Checked += (_, _) => _host.Navigation.Navigate(id);
            _navButtons[id] = b;
            Nav.Children.Add(b);
        }

        _host.Navigation.SetFactory(CreatePage);
        _host.Navigation.Navigated += (id, view) =>
        {
            PageHost.Content = view;
            _navButtons[id].IsChecked = true;
            // Restrained page transition: a short fade only (no layout animation).
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        };
        _host.Navigation.Navigate(_host.Navigation.Current);
    }

    private FrameworkElement CreatePage(PageId id) => id switch
    {
        PageId.Soundboard => new SoundboardPage(_host.Soundboard),
        PageId.Mixer => new MixerPage(_host.Config),
        PageId.Shortcuts => new ShortcutsPage(_host.Soundboard),
        PageId.Settings => new SettingsPage(_host.Config, this, _host.Soundboard),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private void OnSearch()
    {
        var q = SearchBox.Text;
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (q.Length > 0 && _host.Navigation.Current != PageId.Soundboard) _host.Navigation.Navigate(PageId.Soundboard);
        if (PageHost.Content is SoundboardPage sb) sb.SetQuery(q);
    }

    private void ApplyCompact(bool compact)
    {
        SidebarColumn.Width = new GridLength(compact ? 84 : 248);
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        LogoSpot.Width = LogoSpot.Height = compact ? 44 : 92;
        foreach (var l in _navLabels) l.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        GamingButton.Tag = compact ? "compact" : null;
        VmText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Footer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnStateChanged()
    {
        var max = WindowState == WindowState.Maximized;
        // A chrome-less maximized window overhangs the screen by the resize border; pad it back in.
        Frame.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = max ? Glyph.Restore : Glyph.Maximize;
        MaxButton.ToolTip = max ? "Restore" : "Maximize";
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing) return;
        e.Cancel = true; // finish shutting down services first, then exit
        _closing = true;
        SavePlacement();
        _mini?.Close();
        await _exit(0);
    }

    private void RestorePlacement()
    {
        var ui = _host.Config.Current.Ui;
        Width = Math.Max(MinWidth, ui.WindowWidth);
        Height = Math.Max(MinHeight, ui.WindowHeight);
        if (ui.WindowLeft is { } l && ui.WindowTop is { } t && IsOnScreen(l, t))
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

    internal static bool IsOnScreen(double l, double t) =>
        l >= SystemParameters.VirtualScreenLeft - 50 && t >= SystemParameters.VirtualScreenTop - 50 &&
        l + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
        t + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

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

    private void ApplyWindowFrame() => WindowFrame.Apply(this);
}

/// <summary>Dark title-bar colours and Windows 11 rounded corners for chrome-less windows.</summary>
internal static class WindowFrame
{
    public static void Apply(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        var round = 2;
        _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE: round */, ref round, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);
}
