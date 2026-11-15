using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BreadBoard.App.Theme;

/// <summary>Small builders shared by pages so layout stays consistent and the XAML stays short.</summary>
internal static class PageKit
{
    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);

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

    /// <summary>Small status chip ("Active", "2 shortcuts aren't active").</summary>
    public static Border Chip(string text, string brush) => new()
    {
        Style = Res<Style>("StatusChip"),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = Res<Brush>(brush) },
    };

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Style = Res<Style>("FlatCard"),
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

    /// <summary>
    /// Runs <paramref name="set"/> when the switch changes by any means (mouse, keyboard, screen reader /
    /// UI Automation) to a value other than <paramref name="current"/>, so refreshing a switch from the
    /// saved settings never writes them back.
    /// </summary>
    public static void OnToggle(ToggleButton toggle, Func<bool> current, Action<bool> set)
    {
        toggle.Checked += (_, _) => { if (!current()) set(true); };
        toggle.Unchecked += (_, _) => { if (current()) set(false); };
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0, 0, 8, 12),
        Focusable = false,
    };

    public static Button Button(string text, string style = "SoftButton", string? glyph = null, string? tooltip = null)
    {
        var b = new Button { Content = text, Style = Res<Style>(style), ToolTip = tooltip };
        if (glyph is not null) Ui.SetIcon(b, glyph);
        return b;
    }
}
