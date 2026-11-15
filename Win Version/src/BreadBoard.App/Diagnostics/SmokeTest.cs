using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.Navigation;
using BreadBoard.Core;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Diagnostics;

/// <summary>
/// --smoke-test: waits for the first frame, visits every page, then closes the window through the
/// normal close path. Writes smoke-result.json next to the config so tests can assert on it.
/// </summary>
public static class SmokeTest
{
    public static async Task RunAsync(AppHost host, Window window)
    {
        var rendered = new TaskCompletionSource();
        window.ContentRendered += (_, _) => rendered.TrySetResult();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        var startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        var startPage = host.Navigation.Current;
        var audioAtStart = host.AudioCreated;

        var visited = new List<string>();
        foreach (var page in Enum.GetValues<PageId>())
        {
            host.Navigation.Navigate(page);
            await Task.Delay(50).ConfigureAwait(true);
            visited.Add(page.ToString());
        }
        host.Navigation.Navigate(PageId.Shortcuts); // so the next launch can verify it was remembered

        // Mini-player: opens, honours the always-on-top setting, closes cleanly.
        var main = (Views.MainWindow)window;
        main.ShowMini();
        await Task.Delay(100).ConfigureAwait(true);
        var miniOpened = main.MiniPlayer is not null;
        var miniTopmost = main.MiniPlayer?.Topmost ?? false;
        main.MiniPlayer?.Close();

        var sound = await SoundScenarioAsync(host).ConfigureAwait(true);
        var gaming = await GamingScenarioAsync(host).ConfigureAwait(true);

        var result = new
        {
            sound,
            gaming,
            audioAtStart,
            startupMs = Math.Round(startupMs),
            startPage = startPage.ToString(),
            pagesVisited = visited,
            miniOpened,
            miniTopmost,
            audioCreated = host.AudioCreated,
            hotkeysCreated = host.HotkeysCreated,
            webView2Loaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true),
        };
        await File.WriteAllTextAsync(Path.Combine(AppPaths.Roaming, "smoke-result.json"), JsonSerializer.Serialize(result)).ConfigureAwait(true);
        Log.Info("Smoke test finished; closing");
        window.Close();
    }

    /// <summary>Import a generated WAV, give it a shortcut, play it, stop it, remove it — through the real engine.</summary>
    private static async Task<object> SoundScenarioAsync(AppHost host)
    {
        var sb = host.Soundboard;
        var wav = WriteTone(Path.Combine(AppPaths.Roaming, "smoke-tone.wav"));
        var added = await sb.ImportFilesAsync([wav], "Smoke").ConfigureAwait(true);
        var item = added.Count == 1 ? added[0] : null;
        if (item is null) return new { imported = false };

        await sb.SetSoundHotkeyAsync(item.Id, "Ctrl+Alt+Shift+F24").ConfigureAwait(true);
        var hotkey = sb.Hotkeys.FirstOrDefault(h => h.OwnerKey == $"sound:{item.Id}");

        sb.Press(item.Id);
        var played = await WaitAsync(() => item.Playing, 1500).ConfigureAwait(true);
        var progressed = await WaitAsync(() => item.Progress > 0.05, 1500).ConfigureAwait(true);
        sb.StopAll();
        var stopped = await WaitAsync(() => !item.Playing, 1000).ConfigureAwait(true);

        var file = sb.Library.Find(item.Id)?.FilePath;
        sb.Remove(item.Id);
        await Task.Delay(100).ConfigureAwait(true);
        return new
        {
            imported = true,
            state = item.State.ToString(),
            durationMs = item.Duration?.TotalMilliseconds,
            hotkey = hotkey?.Combo,
            hotkeyError = hotkey?.Error,
            engineOk = sb.EngineOk,
            engine = sb.EngineSummary,
            played,
            progressed,
            stopped,
            removed = sb.Items.Count == 0 && file is not null && !File.Exists(file),
        };
    }

    /// <summary>Sequence of two sounds, random play, per-sound route, profile switch round trip.</summary>
    private static async Task<object> GamingScenarioAsync(AppHost host)
    {
        var sb = host.Soundboard;
        var a = WriteTone(Path.Combine(AppPaths.Roaming, "smoke-a.wav"));
        var b = WriteTone(Path.Combine(AppPaths.Roaming, "smoke-b.wav"));
        var added = await sb.ImportFilesAsync([a, b], "Smoke").ConfigureAwait(true);
        if (added.Count != 2) return new { imported = added.Count };
        var seq = await sb.AddSequenceAsync([added[0].Id, added[1].Id]).ConfigureAwait(true);
        var seqMs = seq?.Duration?.TotalMilliseconds;

        // Route one sound to headphones only and give it a shortcut in this profile.
        var e = sb.Library.Find(added[0].Id)!;
        await sb.SaveAsync(e.Id, new Services.SoundEdit(e.Name, e.Category, "Ctrl+Alt+Shift+F23", e.Mode, 5, e.Favorite, e.Icon, e.Tint, Core.Models.SoundRoute.MonitorOnly)).ConfigureAwait(true);
        var random = sb.PlayRandom([seq!]);
        var randomPlayed = await WaitAsync(() => seq!.Playing, 1500).ConfigureAwait(true);
        sb.StopAll();

        var before = sb.ActiveProfile;
        await sb.SwitchProfileAsync("Gaming").ConfigureAwait(true);
        var inGaming = (hotkey: sb.Library.Find(e.Id)!.Hotkey, route: sb.Library.Find(e.Id)!.Route.ToString());
        await sb.SwitchProfileAsync(before).ConfigureAwait(true);
        var back = (hotkey: sb.Library.Find(e.Id)!.Hotkey, route: sb.Library.Find(e.Id)!.Route.ToString());

        sb.Remove(added[1].Id); // sequence of one is dissolved
        var seqGone = sb.Get(seq!.Id) is null;
        sb.Remove(added[0].Id);
        return new
        {
            sequenceMs = seqMs,
            sequenceState = seq.State.ToString(),
            randomPicked = random?.Id == seq.Id,
            randomPlayed,
            profileBefore = before,
            gamingHotkey = inGaming.hotkey,
            gamingRoute = inGaming.route,
            backHotkey = back.hotkey,
            backRoute = back.route,
            seqGone,
            profileCount = sb.Config.Profiles.Count,
        };
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) return false;
            await Task.Delay(20).ConfigureAwait(true);
        }
        return true;
    }

    /// <summary>A quiet 440 Hz tone (amplitude 0 = silence), 16-bit mono PCM. Also used by the screenshot tour.</summary>
    internal static string WriteTone(string path, double seconds = 1, int amplitude = 2000)
    {
        const int rate = 44100;
        var n = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        for (var i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * amplitude));
        return path;
    }
}
