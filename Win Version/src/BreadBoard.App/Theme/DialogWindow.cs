using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using static BreadBoard.App.Theme.PageKit;

namespace BreadBoard.App.Theme;

/// <summary>Dark, chrome-less modal shell shared by Bread Board dialogs (title, body, button row).</summary>
public class DialogWindow : Window
{
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ContentControl _body = new() { Focusable = false };

    public DialogWindow(string title, double width = 460)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Res<Brush>("SidebarBrush");
        Foreground = Res<Brush>("InkBrush");
        FontFamily = Res<FontFamily>("UiFont");
        FontSize = 14;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 52, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        Owner = Application.Current.MainWindow is { IsVisible: true } main ? main : null;
        if (Owner is null) WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var close = new Button { Style = Res<Style>("IconButton"), Content = Glyph.Close, Width = 32, Height = 32, FontSize = 11, Background = Brushes.Transparent, ToolTip = "Close", IsCancel = true };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close();
        var head = new DockPanel { Margin = new Thickness(22, 14, 12, 4) };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = title, Style = Res<Style>("H2"), VerticalAlignment = VerticalAlignment.Center });

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var foot = new Border { Padding = new Thickness(22, 14, 22, 18), Child = _buttons };
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);
        root.Children.Add(new Border { Padding = new Thickness(22, 8, 22, 4), Child = _body });
        Content = new Border { BorderBrush = Res<Brush>("LineBrush"), BorderThickness = new Thickness(1), Child = root };

        SourceInitialized += (_, _) => WindowFrame.Apply(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) Close(); };
    }

    protected object? Body { get => _body.Content; set => _body.Content = value; }

    protected Button AddButton(string text, bool primary = false, bool isDefault = false)
    {
        var b = Button(text, primary ? "PrimaryButton" : "SoftButton");
        b.IsDefault = isDefault;
        b.Margin = new Thickness(10, 0, 0, 0);
        b.MinWidth = 96;
        _buttons.Children.Add(b);
        return b;
    }


    /// <summary>Label above a field.</summary>
    protected static StackPanel Field(string label, UIElement control, string? hint = null)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        p.Children.Add(Text(label, 13, weight: FontWeights.Medium));
        if (control is FrameworkElement fe) fe.Margin = new Thickness(0, 6, 0, 0);
        p.Children.Add(control);
        if (hint is not null)
        {
            var h = Text(hint, 12, muted: true);
            h.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(h);
        }
        return p;
    }

    /// <summary>Simple yes/no confirmation in the app's style.</summary>
    public static bool Confirm(string title, string message, string okText, bool danger = false)
    {
        var d = new DialogWindow(title, 420);
        d.Body = Text(message, 14);
        d.AddButton("Cancel");
        var ok = d.AddButton(okText, primary: !danger, isDefault: true);
        if (danger) ok.Foreground = Res<Brush>("BadBrush");
        ok.Click += (_, _) => d.DialogResult = true;
        return d.ShowDialog() == true;
    }
}
