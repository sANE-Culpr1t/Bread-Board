using System.Runtime.InteropServices;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;
using BreadBoard.Platform.Hotkeys;

namespace BreadBoard.Tests;

/// <summary>Writes small 16-bit PCM WAV files for tests.</summary>
internal static class TestWav
{
    public static string Write(string path, double seconds = 0.5, int rate = 44100, int channels = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var frames = (int)(seconds * rate);
        using var w = new BinaryWriter(File.Create(path));
        var data = frames * channels * 2;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        for (var i = 0; i < frames; i++)
        {
            var s = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 12000);
            for (var c = 0; c < channels; c++) w.Write(s);
        }
        return path;
    }
}

public sealed class SoundLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bb-lib-" + Guid.NewGuid().ToString("N"));
    private readonly ConfigService _config;
    private readonly SoundLibrary _lib;

    public SoundLibraryTests()
    {
        _config = new ConfigService(new ConfigStore(Path.Combine(_dir, "config.json")), debounceMs: 50);
        _lib = new SoundLibrary(_config, Path.Combine(_dir, "Sounds"));
    }

    private string Source(string name) => TestWav.Write(Path.Combine(_dir, "src", name));

    [Fact]
    public void Import_copies_into_library_and_names_from_file()
    {
        var src = Source("vine_boom-loud.wav");
        var e = _lib.Import(src, category: "Memes");
        Assert.Equal("vine boom loud", e.Name);
        Assert.Equal("Memes", e.Category);
        Assert.StartsWith(_lib.LibraryDir, e.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(e.FilePath));
        File.Delete(src); // the original can go away; the pad keeps working
        Assert.False(SoundLibrary.FileMissing(e));

        var again = _lib.Import(Source("vine_boom-loud.wav"));
        Assert.NotEqual(e.FilePath, again.FilePath); // no overwrite on name clash
        Assert.Equal(2, _lib.Sounds.Count);
    }

    [Fact]
    public void Rejects_unsupported_and_missing_files()
    {
        var txt = Path.Combine(_dir, "notes.txt");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(txt, "hi");
        Assert.Throws<NotSupportedException>(() => _lib.Import(txt));
        Assert.Throws<FileNotFoundException>(() => _lib.Import(Path.Combine(_dir, "gone.wav")));
        Assert.Empty(_lib.Sounds);
    }

    [Fact]
    public void Update_normalises_and_registers_new_category()
    {
        var e = _lib.Import(Source("a.wav"));
        _lib.Update(e.Id, s => { s.Name = "  Air   horn  "; s.Hotkey = "shift+ctrl+h"; s.Volume = 140; s.Category = " Party "; });
        var s = _lib.Find(e.Id)!;
        Assert.Equal("Air horn", s.Name);
        Assert.Equal("Ctrl+Shift+H", s.Hotkey);
        Assert.Equal(100, s.Volume);
        Assert.Equal("Party", s.Category);
        Assert.Contains("Party", _lib.Categories());
    }

    [Fact]
    public void Remove_deletes_library_copy_and_recent_entry()
    {
        var e = _lib.Import(Source("b.wav"));
        _lib.MarkPlayed(e.Id);
        Assert.True(_lib.Remove(e.Id));
        Assert.False(File.Exists(e.FilePath));
        Assert.Empty(_lib.Sounds);
        Assert.DoesNotContain(e.Id, _config.Current.Recent);
        Assert.False(_lib.Remove(e.Id));
    }

    [Fact]
    public void Remove_never_deletes_files_outside_the_library()
    {
        var outside = Source("outside.wav");
        var e = _lib.Add(outside, "Outside", null, null);
        _lib.Remove(e.Id);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Recent_is_newest_first_without_duplicates_and_capped()
    {
        for (var i = 0; i < 20; i++) _lib.MarkPlayed($"s{i}");
        _lib.MarkPlayed("s5");
        var r = _config.Current.Recent;
        Assert.Equal(SoundLibrary.MaxRecent, r.Count);
        Assert.Equal("s5", r[0]);
        Assert.Equal("s19", r[1]);
        Assert.Single(r, x => x == "s5");
    }

    [Fact]
    public void Favourite_toggles_and_missing_file_is_detected_and_relocated()
    {
        var e = _lib.Import(Source("c.wav"));
        _lib.ToggleFavorite(e.Id);
        Assert.True(_lib.Find(e.Id)!.Favorite);
        _lib.ToggleFavorite(e.Id);
        Assert.False(_lib.Find(e.Id)!.Favorite);

        File.Delete(e.FilePath);
        Assert.True(SoundLibrary.FileMissing(_lib.Find(e.Id)!));
        Assert.Throws<FileNotFoundException>(() => SoundLoader.Load(e.Id, e.FilePath));

        _lib.Relocate(e.Id, Source("c-found.wav"));
        Assert.False(SoundLibrary.FileMissing(_lib.Find(e.Id)!));
    }

    [Fact]
    public void Settings_survive_restart()
    {
        var e = _lib.Import(Source("d.wav"));
        _lib.Update(e.Id, s => { s.Hotkey = "Num1"; s.Mode = PlayMode.Hold; s.Favorite = true; });
        _config.Dispose();
        var reloaded = new ConfigStore(Path.Combine(_dir, "config.json")).Load();
        var s = Assert.Single(reloaded.Sounds);
        Assert.Equal(("Num1", PlayMode.Hold, true), (s.Hotkey, s.Mode, s.Favorite));
    }

    public void Dispose()
    {
        _config.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

public class SoundPlaybackTests
{
    private static SoundClip Clip(string id, int frames, float value = 0.25f) => new(id, Enumerable.Repeat(value, frames * 2).ToArray());

    [Fact]
    public void Decodes_wav_to_48k_stereo()
    {
        var path = TestWav.Write(Path.Combine(Path.GetTempPath(), "bb-dec-" + Guid.NewGuid().ToString("N") + ".wav"), seconds: 1, rate: 22050, channels: 1);
        try
        {
            var clip = SoundLoader.Load("x", path);
            Assert.InRange(clip.Duration.TotalSeconds, 0.97, 1.03);
            Assert.Equal(0, clip.Samples.Length % 2);
            Assert.Contains(clip.Samples, v => MathF.Abs(v) > 0.2f);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Unsupported_extension_is_reported()
    {
        var path = Path.Combine(Path.GetTempPath(), "bb-" + Guid.NewGuid().ToString("N") + ".flac");
        File.WriteAllBytes(path, [1, 2, 3]);
        try { Assert.Throws<NotSupportedException>(() => SoundLoader.Load("x", path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Different_sounds_play_simultaneously_and_sum()
    {
        var e = new SoundboardEngine();
        e.Register("a", Clip("a", 100, 0.25f), PlayMode.OneShot, 100);
        e.Register("b", Clip("b", 100, 0.5f), PlayMode.OneShot, 100);
        e.Trigger("a"); e.Trigger("b");
        var buf = new float[32];
        e.Render(buf);
        Assert.Equal(0.75f, buf[0], 3);
        Assert.Equal(2, e.Playing.Count);
    }

    [Fact]
    public void Rapid_triggers_stay_within_voice_limit_and_stop_all_silences()
    {
        var e = new SoundboardEngine { MaxVoices = 8 };
        e.Register("a", Clip("a", 10_000, 0.01f), PlayMode.OneShot, 100);
        var buf = new float[256];
        for (var i = 0; i < 500; i++)
        {
            e.Trigger("a");
            if (i % 10 == 0) e.Render(buf);
        }
        e.Render(buf);
        Assert.InRange(buf[0], 0.079f, 0.081f); // exactly 8 voices of 0.01
        e.StopAll();
        e.Render(buf);
        Assert.All(buf, v => Assert.Equal(0f, v));
        Assert.Empty(e.Playing);
    }

    [Fact]
    public void Progress_advances_and_stop_ends_one_sound()
    {
        var e = new SoundboardEngine();
        e.Register("a", Clip("a", 1000), PlayMode.Loop, 100);
        e.Register("b", Clip("b", 1000), PlayMode.Loop, 100);
        e.Trigger("a"); e.Trigger("b");
        var buf = new float[200];
        e.Render(buf); e.Render(buf);
        Assert.InRange(e.GetProgress("a"), 0.09, 0.11); // 200 of 2000 samples recorded at the start of the 2nd block
        e.Stop("a");
        e.Render(buf);
        Assert.Equal(["b"], e.Playing);
    }

    [Fact]
    public void Update_changes_mode_and_volume_live()
    {
        var e = new SoundboardEngine();
        e.Register("a", Clip("a", 1000, 0.5f), PlayMode.OneShot, 100);
        e.Update("a", PlayMode.Toggle, 50);
        e.Trigger("a");
        var buf = new float[8];
        e.Render(buf);
        Assert.Equal(0.25f, buf[0], 3);
        e.Trigger("a"); e.Render(buf); // toggle: second press stops
        Assert.Empty(e.Playing);
    }

    [Fact]
    public void Unregistered_sound_is_ignored()
    {
        var e = new SoundboardEngine();
        e.Trigger("nope");
        var buf = new float[8];
        e.Render(buf);
        Assert.Empty(e.Playing);
    }
}

public class GlobalHotkeyTests
{
    [Theory]
    [InlineData("A")] [InlineData("7")] [InlineData("Num1")] [InlineData("F13")] [InlineData("Space")] [InlineData("PageDown")] [InlineData("/")]
    public void Key_names_round_trip(string name) =>
        Assert.Equal(name, GlobalHotkeyService.KeyName(GlobalHotkeyService.VirtualKey(name)!.Value));

    [Fact]
    public async Task Rejects_reserved_and_reports_conflicts_between_programs()
    {
        using var a = new GlobalHotkeyService();
        using var b = new GlobalHotkeyService(); // stands in for "another program" (separate registrations)
        Assert.NotNull(await a.RegisterAsync(new Hotkey(HotkeyModifiers.Alt, "F4"), () => { }));

        var combo = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "F23");
        Assert.Null(await a.RegisterAsync(combo, () => { }));
        var err = await b.RegisterAsync(combo, () => { });
        Assert.NotNull(err);
        Assert.Contains("already used", err, StringComparison.Ordinal);

        a.UnregisterAll();
        await Task.Delay(50);
        Assert.Null(await b.RegisterAsync(combo, () => { })); // freed after unregister
    }

    [Fact]
    public async Task Fires_while_another_window_has_focus()
    {
        // RegisterHotKey is system-wide: a synthetic key press reaches us no matter which window is active.
        using var svc = new GlobalHotkeyService();
        var fired = new TaskCompletionSource();
        var released = new TaskCompletionSource();
        var combo = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "F22");
        Assert.Null(await svc.RegisterAsync(combo, () => fired.TrySetResult(), () => released.TrySetResult()));

        const ushort ctrl = 0x11, alt = 0x12, shift = 0x10, f22 = 0x85;
        Send(ctrl, false); Send(alt, false); Send(shift, false); Send(f22, false);
        await Task.Delay(60);
        Send(f22, true); Send(shift, true); Send(alt, true); Send(ctrl, true);

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await released.Task.WaitAsync(TimeSpan.FromSeconds(3)); // hold-mode release detection
    }

    private static void Send(ushort vk, bool up)
    {
        var input = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } };
        Assert.Equal(1u, SendInput(1, [input], Marshal.SizeOf<INPUT>()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
