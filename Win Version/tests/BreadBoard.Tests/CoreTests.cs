using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("ctrl+shift+s", "Ctrl+Shift+S")]
    [InlineData("Shift+Ctrl+M", "Ctrl+Shift+M")]
    [InlineData("1", "1")]
    [InlineData("Num5", "Num5")]
    public void Parses_and_normalises(string input, string expected)
    {
        Assert.True(Hotkey.TryParse(input, out var hk));
        Assert.Equal(expected, hk.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hyper+K")]
    [InlineData("Ctrl+")]
    public void Rejects_invalid(string input) => Assert.False(Hotkey.TryParse(input, out _));

    [Fact]
    public void Refuses_reserved_and_win_combos()
    {
        var cfg = new AppConfig();
        Assert.NotNull(HotkeyRules.Validate(cfg, "Alt+F4", "sound:x"));
        Assert.NotNull(HotkeyRules.Validate(cfg, "Win+K", "sound:x"));
    }

    [Fact]
    public void Refuses_duplicates_but_allows_reassigning_own_key()
    {
        var cfg = new AppConfig();
        cfg.Sounds.Add(new SoundEntry { Id = "a", Name = "Airhorn", Hotkey = "3" });
        Assert.Contains("Airhorn", HotkeyRules.Validate(cfg, "3", "sound:b"));
        Assert.Null(HotkeyRules.Validate(cfg, "3", "sound:a"));
        Assert.NotNull(HotkeyRules.Validate(cfg, "Ctrl+Shift+S", "sound:a")); // default Stop All
    }

    [Fact]
    public void Finds_duplicates_from_imported_config()
    {
        var cfg = new AppConfig();
        cfg.Sounds.Add(new SoundEntry { Name = "A", Hotkey = "ctrl+1" });
        cfg.Sounds.Add(new SoundEntry { Name = "B", Hotkey = "Ctrl+1" });
        Assert.Contains("Ctrl+1", HotkeyRules.Duplicates(cfg));
    }
}

public class SoundLinkTests
{
    [Fact]
    public void Myinstants_page()
    {
        Assert.True(SoundLinkParser.TryParse("https://www.myinstants.com/en/instant/vine-boom-sound-70972/", out var link, out _));
        Assert.Equal(SoundLinkKind.MyinstantsPage, link!.Kind);
        Assert.Equal("Vine Boom Sound", link.SuggestedName);
    }

    [Fact]
    public void Direct_file_without_scheme()
    {
        Assert.True(SoundLinkParser.TryParse("example.com/sfx/air_horn.mp3", out var link, out _));
        Assert.Equal(SoundLinkKind.DirectFile, link!.Kind);
        Assert.Equal("Air Horn", link.SuggestedName);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("not a url")]
    [InlineData("ftp://x.com/a.mp3")]
    public void Rejects_unsupported(string input)
    {
        Assert.False(SoundLinkParser.TryParse(input, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bb-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Round_trips_config()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var cfg = new AppConfig();
        cfg.Sounds.Add(new SoundEntry { Name = "Bruh", Mode = PlayMode.Hold, Hotkey = "1" });
        cfg.Devices.AppSource = new AppSourceSelection { ProcessName = "brave" };
        store.Save(cfg);

        var loaded = store.Load();
        Assert.Equal("Bruh", loaded.Sounds[0].Name);
        Assert.Equal(PlayMode.Hold, loaded.Sounds[0].Mode);
        Assert.Equal("brave", loaded.Devices.AppSource!.ProcessName);
        Assert.Contains("\"hold\"", File.ReadAllText(store.Path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_is_backed_up()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ not json");
        var cfg = new ConfigStore(path).Load();
        Assert.Empty(cfg.Sounds);
        Assert.Single(Directory.GetFiles(_dir, "config.json.bad-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
