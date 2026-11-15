using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using BreadBoard.Core.Config;

namespace BreadBoard.App.Views;

/// <summary>
/// Floating Gaming Mode window: draggable by its header, resizable from any edge, optional
/// always-on-top. Size, position and pin state are remembered. Audio controls go live later.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private readonly ConfigService _config;
    private double _expandedHeight;

    public MiniPlayerWindow(ConfigService config)
    {
        _config = config;
        InitializeComponent();
        var ui = config.Current.Ui;
        Width = Math.Max(MinWidth, ui.MiniWidth);
        Height = Math.Max(MinHeight, ui.MiniHeight);
        if (ui.MiniLeft is { } l && ui.MiniTop is { } t && MainWindow.IsOnScreen(l, t))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 96;
        }
        SetTopmost(ui.MiniTopmost);

        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        PinButton.Click += (_, _) =>
        {
            var on = PinButton.IsChecked == true;
            Topmost = on;
            _config.Update(c => c.Ui.MiniTopmost = on);
        };
        CollapseButton.Click += (_, _) => ToggleCollapsed();
        CloseButton.Click += (_, _) => Close();
        SourceInitialized += (_, _) => WindowFrame.Apply(this);
    }

    public bool IsCollapsed => Body.Visibility != Visibility.Visible;

    public void SetTopmost(bool on)
    {
        Topmost = on;
        PinButton.IsChecked = on;
        PinButton.ToolTip = on ? "Kept on top — click to release" : "Keep on top";
    }

    private void ToggleCollapsed()
    {
        if (!IsCollapsed)
        {
            _expandedHeight = Height;
            Body.Visibility = Visibility.Collapsed;
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
            CollapseButton.Content = "";
            CollapseButton.ToolTip = "Expand";
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            Body.Visibility = Visibility.Visible;
            MinHeight = 300;
            Height = Math.Max(300, _expandedHeight);
            CollapseButton.Content = "";
            CollapseButton.ToolTip = "Collapse";
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        var h = IsCollapsed ? _expandedHeight : Height;
        _config.Update(c =>
        {
            c.Ui.MiniLeft = Left;
            c.Ui.MiniTop = Top;
            c.Ui.MiniWidth = Width;
            c.Ui.MiniHeight = h;
        });
    }
}
