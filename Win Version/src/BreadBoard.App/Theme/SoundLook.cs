using System.Windows.Media;

namespace BreadBoard.App.Theme;

/// <summary>Pad icons and colours a sound can use (stored by key in the config).</summary>
public static class SoundLook
{
    public static readonly (string Key, string Glyph)[] Icons =
    [
        ("volume", ""), ("emoji", ""), ("bolt", ""),
        ("music", ""), ("star", ""), ("heart", ""), ("game", ""),
        ("alert", ""), ("chat", ""),
    ];

    public static readonly (string Key, string Hex)[] Tints =
    [
        ("beige", "#E3C8A4"), ("amber", "#D9A35B"), ("cream", "#EFE8DD"), ("slate", "#9AA7B8"),
        ("clay", "#C98A72"), ("sage", "#9FB59A"), ("mauve", "#B59AB0"),
    ];

    private static readonly Dictionary<string, Brush> Brushes = Tints.ToDictionary(t => t.Key, t =>
    {
        var b = (Brush)new BrushConverter().ConvertFromString(t.Hex)!;
        b.Freeze();
        return b;
    });

    public static string Glyph(string key) => Array.Find(Icons, i => i.Key == key).Glyph ?? Icons[0].Glyph;
    public static Brush Brush(string key) => Brushes.TryGetValue(key, out var b) ? b : Brushes["beige"];
}
