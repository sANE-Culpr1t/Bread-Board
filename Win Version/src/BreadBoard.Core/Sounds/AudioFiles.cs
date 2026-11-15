namespace BreadBoard.Core.Sounds;

/// <summary>Which audio files Bread Board accepts, and how library files are named.</summary>
public static class AudioFiles
{
    public static readonly string[] Extensions = [".wav", ".mp3", ".ogg"];

    /// <summary>For messages: "WAV, MP3 or OGG".</summary>
    public const string Description = "WAV, MP3 or OGG";

    /// <summary>Open-file dialog filter.</summary>
    public static readonly string DialogFilter = $"Audio ({string.Join(";", Extensions.Select(e => "*" + e))})|{string.Join(";", Extensions.Select(e => "*" + e))}";

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>A path in <paramref name="dir"/> for <paramref name="name"/> + <paramref name="ext"/> that is a valid file name and doesn't exist yet.</summary>
    public static string UniquePath(string dir, string name, string ext)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string([.. name.Select(c => bad.Contains(c) ? '_' : c)]).Trim();
        if (clean.Length == 0) clean = "sound";
        ext = ext.ToLowerInvariant();
        var path = Path.Combine(dir, clean + ext);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{clean} ({i}){ext}");
        return path;
    }
}
