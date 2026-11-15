using System.Windows;
using System.Windows.Media;

namespace BreadBoard.App.Theme;

/// <summary>Attached properties shared by the control templates (hover colour, corner radius, icon glyph).</summary>
public static class Ui
{
    public static readonly DependencyProperty HoverBrushProperty =
        DependencyProperty.RegisterAttached("HoverBrush", typeof(Brush), typeof(Ui), new PropertyMetadata(null));
    public static Brush? GetHoverBrush(DependencyObject o) => (Brush?)o.GetValue(HoverBrushProperty);
    public static void SetHoverBrush(DependencyObject o, Brush? v) => o.SetValue(HoverBrushProperty, v);

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new PropertyMetadata(new CornerRadius(20)));
    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius v) => o.SetValue(CornerRadiusProperty, v);

    /// <summary>Segoe Fluent Icons glyph shown before a button's content.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new PropertyMetadata(null));
    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);
}

/// <summary>Segoe Fluent Icons / MDL2 glyphs used across the app (one place to change them).</summary>
public static class Glyph
{
    public const string Grid = "";
    public const string Mixer = "";
    public const string Keyboard = "";
    public const string Settings = "";
    public const string Search = "";
    public const string Add = "";
    public const string Play = "";
    public const string Stop = "";
    public const string Previous = "";
    public const string Next = "";
    public const string Mic = "";
    public const string MicOff = "";
    public const string Volume = "";
    public const string Mute = "";
    public const string Pin = "";
    public const string Unpin = "";
    public const string Star = "";
    public const string StarFill = "";
    public const string Game = "";
    public const string App = "";
    public const string Bolt = "";
    public const string Clock = "";
    public const string Repeat = "";
    public const string Headphones = "";
    public const string Close = "";
    public const string Minimize = "";
    public const string Maximize = "";
    public const string Restore = "";
    public const string Info = "";
    public const string Waveform = "";
}
