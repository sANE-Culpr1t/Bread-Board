using System.Globalization;
using System.Text.RegularExpressions;

namespace BreadBoard.Core.Sounds;

public enum SoundLinkKind { DirectFile, MyinstantsPage }

public sealed record SoundLink(SoundLinkKind Kind, Uri Url, string SuggestedName, string FileName);

/// <summary>Recognises links that can be imported from the web (same rules as the prototype).</summary>
public static partial class SoundLinkParser
{

    public static bool TryParse(string? raw, out SoundLink? link, out string? error)
    {
        link = null;
        error = null;
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) { error = "Enter a link."; return false; }
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.'))
        {
            error = "That doesn't look like a web address.";
            return false;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var file = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (AudioFiles.IsSupported(file))
        {
            link = new(SoundLinkKind.DirectFile, uri, Title(Path.GetFileNameWithoutExtension(file)), file);
            return true;
        }

        var m = InstantPath().Match(path);
        if (uri.Host.EndsWith("myinstants.com", StringComparison.OrdinalIgnoreCase) && m.Success)
        {
            var slug = TrailingId().Replace(m.Groups[1].Value, "");
            link = new(SoundLinkKind.MyinstantsPage, uri, Title(slug), slug + ".mp3");
            return true;
        }

        error = $"Paste a Myinstants sound page link or a direct link to a {AudioFiles.Description} file.";
        return false;
    }

    private static string Title(string slug)
    {
        var words = Separators().Replace(slug, " ").Trim();
        var t = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words);
        return t.Length > SoundLibrary.MaxNameLength ? t[..SoundLibrary.MaxNameLength] : t;
    }

    [GeneratedRegex(@"/instant/([^/]+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex InstantPath();

    [GeneratedRegex(@"-\d+$")]
    private static partial Regex TrailingId();

    [GeneratedRegex(@"[-_]+")]
    private static partial Regex Separators();
}
