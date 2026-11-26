using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using static BreadBoard.App.Theme.PageKit;

namespace BreadBoard.App.Shell;

/// <summary>Small panel that opens above the tray icon: status, mic mute, stop all, open and quit. Hides when you click elsewhere.</summary>
public sealed class TrayFlyout : Window
{
    private readonly SoundboardController _sb;
    private readonly TextBlock _status = Text("", 12, muted: true);
    private readonly System.Windows.Shapes.Ellipse _dot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _mic;
    private bool _closing;
    private DateTime _hiddenAt;

    public TrayFlyout(SoundboardController sb, Action open, Action quit)
    {
        _sb = sb;
        Title = "Bread Board";
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Res<Brush>("SidebarBrush");
        Foreground = Res<Brush>("InkBrush");
        FontFamily = Res<FontFamily>("UiFont");
        UseLayoutRounding = true;

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Image { Source = Res<ImageSource>("LogoImage"), Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0) });
        head.Children.Add(Text("Bread Board", 16, weight: FontWeights.SemiBold));
        root.Children.Add(head);
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 12) };
        status.Children.Add(_dot);
        status.Children.Add(_status);
        root.Children.Add(status);

        _mic = PageKit.Button("Mute mic", glyph: Glyph.MicOff);
        _mic.HorizontalAlignment = HorizontalAlignment.Stretch;
        _mic.Click += (_, _) => _sb.ToggleMicMute();
        var stop = PageKit.Button("Stop all sounds", glyph: Glyph.Stop);
        stop.HorizontalAlignment = HorizontalAlignment.Stretch;
        stop.Margin = new Thickness(0, 8, 0, 0);
        stop.Click += (_, _) => _sb.StopAll();
        root.Children.Add(_mic);
        root.Children.Add(stop);

        var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        var openB = PageKit.Button("Open", "PrimaryButton");
        openB.Click += (_, _) => { Hide(); open(); };
        var quitB = PageKit.Button("Quit", "GhostButton", tooltip: "Close Bread Board completely; the Bread Board mic disappears until it starts again");
        quitB.Click += (_, _) => { Hide(); quit(); };
        Grid.SetColumn(quitB, 2);
        bottom.Children.Add(openB);
        bottom.Children.Add(quitB);
        root.Children.Add(bottom);
        Content = new Border { BorderBrush = Res<Brush>("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = root };

        Deactivated += (_, _) => { Hide(); _hiddenAt = DateTime.UtcNow; };
        SourceInitialized += (_, _) => WindowFrame.Apply(this);
        sb.EngineChanged += Refresh;
        sb.MixChanged += Refresh;
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 300) return; // this click on the icon is what closed it
        Refresh();
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea; // above the taskbar, at the notification-area corner
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
        Activate();
    }

    private void Refresh()
    {
        var live = _sb.Engine == EngineState.Live;
        _status.Text = live ? "Bread Board Mic is live" : _sb.EngineSummary;
        _dot.Fill = Res<Brush>(live ? "OkBrush" : _sb.Engine == EngineState.Problem ? "BadBrush" : "MuteBrush");
        var muted = _sb.Config.Channels.Mic.Muted;
        _mic.Content = muted ? "Unmute mic" : "Mute mic";
        _mic.Foreground = Res<Brush>(muted ? "BadBrush" : "InkBrush");
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing) return;
        _closing = true;
        _sb.EngineChanged -= Refresh;
        _sb.MixChanged -= Refresh;
    }
}
