using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.Tests;

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
        Assert.Equal(2, _config.Current.Sounds.Count);
    }

    [Fact]
    public void Rejects_unsupported_and_missing_files()
    {
        var txt = Path.Combine(_dir, "notes.txt");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(txt, "hi");
        Assert.Throws<NotSupportedException>(() => _lib.Import(txt));
        Assert.Throws<FileNotFoundException>(() => _lib.Import(Path.Combine(_dir, "gone.wav")));
        Assert.Empty(_config.Current.Sounds);
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
        Assert.Empty(_config.Current.Sounds);
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
