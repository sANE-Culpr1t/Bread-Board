using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreadBoard.App.Theme;

namespace BreadBoard.App.Views.Pages;

/// <summary>Small builders shared by pages so layout stays consistent and the XAML stays short.</summary>
internal static class PageKit
{
    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    public static TextBlock Text(string s, double size = 14, bool muted = false, FontWeight? weight = null) => new()
    {
        Text = s,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = muted ? Res<Brush>("MuteBrush") : Res<Brush>("InkBrush"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Icon(string glyph, double size = 18, string brush = "InkBrush") => new()
    {
        Text = glyph,
        Style = Res<Style>("Icon"),
        FontSize = size,
        Foreground = Res<Brush>(brush),
    };

    /// <summary>Page title row with an optional description.</summary>
    public static StackPanel Header(string title, string description)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        p.Children.Add(new TextBlock { Text = title, Style = Res<Style>("H1") });
        p.Children.Add(new TextBlock { Text = description, Style = Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        return p;
    }

    /// <summary>"Arrives in Iteration N" chip, so disabled controls explain themselves.</summary>
    public static Border Pending(string text) => new()
    {
        Style = Res<Style>("PendingChip"),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = Res<Brush>("Accent2Brush") },
    };

    public static Border Card(UIElement child, Thickness? padding = null, bool flat = true) => new()
    {
        Style = Res<Style>(flat ? "FlatCard" : "Card"),
        Padding = padding ?? new Thickness(20, 8, 20, 8),
        Child = child,
    };

    public static TextBlock SectionTitle(string s) => new()
    {
        Text = s,
        Style = Res<Style>("H2"),
        Margin = new Thickness(0, 22, 0, 10),
    };

    /// <summary>Settings-style row: title + description on the left, control on the right.</summary>
    public static Border Row(string title, string? description, FrameworkElement control, bool last = false)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 12, 0, 12) };
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(title, 15));
        if (description is not null) text.Children.Add(Text(description, 13, muted: true));
        dock.Children.Add(text);
        return new Border
        {
            Child = dock,
            BorderBrush = Res<Brush>("LineBrush"),
            BorderThickness = last ? new Thickness(0) : new Thickness(0, 0, 0, 1),
        };
    }

    public static CheckBox Switch(bool on, bool enabled, string tooltip) => new()
    {
        Style = Res<Style>("Switch"),
        IsChecked = on,
        IsEnabled = enabled,
        ToolTip = tooltip,
    };

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0, 0, 8, 12),
        Focusable = false,
    };

    public static Button Button(string text, string style = "SoftButton", string? glyph = null, bool enabled = true, string? tooltip = null)
    {
        var b = new Button { Content = text, Style = Res<Style>(style), IsEnabled = enabled, ToolTip = tooltip };
        if (glyph is not null) Ui.SetIcon(b, glyph);
        return b;
    }
}
