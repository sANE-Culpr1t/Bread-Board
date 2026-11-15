using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Sounds;

namespace BreadBoard.Audio.Import;

public sealed record DownloadedSound(string FilePath, string SuggestedName, Uri SourceUrl);

/// <summary>
/// Downloads a sound from a supported link (Myinstants page or direct .mp3/.wav/.ogg) into the
/// library folder. Pages are only read to find their audio file link; nothing is scraped beyond that.
/// </summary>
public sealed partial class SoundDownloader(HttpClient http)
{
    public const long MaxBytes = 25 * 1024 * 1024;
    private const string TooBig = "The sound file is larger than 25 MB.";

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BreadBoard", "0.1"));
        return http;
    }

    public async Task<DownloadedSound> DownloadAsync(string link, string libraryDir, CancellationToken ct = default)
    {
        if (!SoundLinkParser.TryParse(link, out var parsed, out var error)) throw new ArgumentException(error, nameof(link));
        var audioUrl = parsed!.Kind == SoundLinkKind.DirectFile ? parsed.Url : await FindAudioOnPageAsync(parsed.Url, ct).ConfigureAwait(false);

        var ext = AudioFiles.IsSupported(audioUrl.AbsolutePath) ? Path.GetExtension(audioUrl.AbsolutePath) : ".mp3";
        Directory.CreateDirectory(libraryDir);
        var path = AudioFiles.UniquePath(libraryDir, parsed.SuggestedName, ext);

        using var resp = await http.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > MaxBytes) throw new InvalidOperationException(TooBig);
        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That link returned a web page, not an audio file.");

        var tmp = path + ".part";
        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > MaxBytes) throw new InvalidOperationException(TooBig);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                }
            }
            File.Move(tmp, path);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        Log.Info($"Downloaded {audioUrl} -> {path}");
        return new DownloadedSound(path, parsed.SuggestedName, parsed.Url);
    }

    private async Task<Uri> FindAudioOnPageAsync(Uri page, CancellationToken ct)
    {
        var html = await http.GetStringAsync(page, ct).ConfigureAwait(false);
        var m = MediaLink().Match(html);
        if (!m.Success) throw new InvalidOperationException("Couldn't find a sound file on that page.");
        return new Uri(page, m.Groups[1].Value);
    }


    // Myinstants pages reference their file as /media/sounds/<name>.mp3 (play button / download link).
    [GeneratedRegex(@"[""'(](/media/sounds/[^""'\s)]+\.(?:mp3|wav|ogg))", RegexOptions.IgnoreCase)]
    private static partial Regex MediaLink();
}
