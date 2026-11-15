using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Models;

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
    public void Clashes_are_found_whatever_the_spelling()
    {
        var cfg = new AppConfig();
        cfg.Sounds.Add(new SoundEntry { Id = "a", Name = "A", Hotkey = "ctrl+1" });
        cfg.Sounds.Add(new SoundEntry { Id = "b", Name = "B", Hotkey = "Ctrl+1" });
        Assert.Contains("“B”", HotkeyRules.Validate(cfg, "CTRL+1", HotkeyRules.SoundOwner("a")));
        Assert.Contains("“Play a random sound”", HotkeyRules.Validate(cfg, "ctrl+shift+r", HotkeyRules.SoundOwner("a")));
    }
}
