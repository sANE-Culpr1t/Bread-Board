using System.Windows.Media;

namespace BreadBoard.App.ViewModels;

/// <summary>
/// A sound pad for layout testing only (Iteration 1). Real pads come from the user's library in
/// Iteration 2. One sample is shown mid-playback so the playing state can be reviewed.
/// </summary>
public sealed record SamplePad(
    string Name, string Glyph, string Tint, string Category, string Shortcut,
    TimeSpan Duration, string Mode, bool Favorite, bool Playing = false, double Progress = 0)
{
    public Brush TintBrush { get; } = (Brush)new BrushConverter().ConvertFromString(Tint)!;
    public string DurationText => Duration.TotalSeconds < 60
        ? $"{Duration.TotalSeconds:0.0} s"
        : $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}";
    public string ModeGlyph => Mode switch { "Loop" => "", "Hold" => "", "Toggle" => "", _ => "" };
    public string StatusText => Playing ? "Playing" : Mode == "One-shot" ? "" : Mode;

    public static IReadOnlyList<SamplePad> All { get; } =
    [
        new("Bruh", "", "#E3C8A4", "Memes", "Num1", TimeSpan.FromSeconds(0.9), "One-shot", true),
        new("Vine Boom", "", "#EFE8DD", "Memes", "Num2", TimeSpan.FromSeconds(1.4), "One-shot", true),
        new("Airhorn", "", "#D9A35B", "Effects", "Num3", TimeSpan.FromSeconds(2.2), "One-shot", false),
        new("Laugh", "", "#D9A35B", "Reactions", "Num4", TimeSpan.FromSeconds(2.6), "One-shot", true),
        new("Startup Chime", "", "#9AA7B8", "Effects", "Num5", TimeSpan.FromSeconds(3.2), "One-shot", false),
        new("Explosion", "", "#C98A72", "Effects", "Num6", TimeSpan.FromSeconds(2.4), "One-shot", false),
        new("Drumroll", "", "#E3C8A4", "Music", "Num7", TimeSpan.FromSeconds(4.0), "Hold", false),
        new("Siren", "", "#C98A72", "Effects", "Num8", TimeSpan.FromSeconds(2.0), "Loop", false, Playing: true, Progress: 0.42),
        new("Applause", "", "#9FB59A", "Reactions", "Num9", TimeSpan.FromSeconds(5.2), "Toggle", false),
        new("Sad Trombone", "", "#B59AB0", "Reactions", "Ctrl+1", TimeSpan.FromSeconds(3.0), "One-shot", false),
    ];
}
