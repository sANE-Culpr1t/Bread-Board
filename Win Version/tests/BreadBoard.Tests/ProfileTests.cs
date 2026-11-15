using BreadBoard.Core.Config;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

public class ProfileTests
{
    private static AppConfig Config()
    {
        var c = new AppConfig();
        c.Sounds.Add(new SoundEntry { Id = "boom", Name = "Boom", Hotkey = "Num1" });
        c.Sounds.Add(new SoundEntry { Id = "horn", Name = "Horn", Hotkey = "Num2", Route = SoundRoute.MicOnly });
        return c;
    }

    [Fact]
    public void Defaults_are_created_once_from_current_settings()
    {
        var c = Config();
        ProfileService.EnsureDefaults(c);
        ProfileService.EnsureDefaults(c);
        Assert.Equal(ProfileService.Names, c.Profiles.Select(p => p.Name));
        Assert.All(c.Profiles, p => Assert.Equal("Num1", p.SoundHotkeys["boom"]));
        Assert.Equal("General use", c.ActiveProfile);
    }

    [Fact]
    public void Switching_keeps_each_profiles_own_settings()
    {
        var c = Config();
        ProfileService.EnsureDefaults(c);

        // Customise "General use" live, then switch to Gaming and customise that.
        c.Channels.Mic.Gain = 120;
        c.Sounds[0].Hotkey = "F1";
        ProfileService.Switch(c, "Gaming");
        Assert.Equal(80, c.Channels.Mic.Gain);         // Gaming still has the original level
        Assert.Equal("Num1", c.Sounds[0].Hotkey);
        c.Channels.Mic.Gain = 60;
        c.Sounds[1].Route = SoundRoute.MonitorOnly;
        c.Processing.PushToTalk.Enabled = true;

        // Back to General use: its changes were kept.
        ProfileService.Switch(c, "General use");
        Assert.Equal(120, c.Channels.Mic.Gain);
        Assert.Equal("F1", c.Sounds[0].Hotkey);
        Assert.Equal(SoundRoute.MicOnly, c.Sounds[1].Route);
        Assert.False(c.Processing.PushToTalk.Enabled);

        // And Gaming's were kept too.
        ProfileService.Switch(c, "Gaming");
        Assert.Equal(60, c.Channels.Mic.Gain);
        Assert.Equal(SoundRoute.MonitorOnly, c.Sounds[1].Route);
        Assert.True(c.Processing.PushToTalk.Enabled);
    }

    [Fact]
    public void Profiles_never_share_objects_with_live_settings()
    {
        var c = Config();
        ProfileService.EnsureDefaults(c);
        c.Channels.Mic.Gain = 5;
        Assert.All(c.Profiles, p => Assert.Equal(80, p.Channels.Mic.Gain));
        ProfileService.Switch(c, "Custom");
        c.Channels.Master.Gain = 1;
        Assert.Equal(90, c.Profiles.Single(p => p.Name == "Custom").Channels.Master.Gain);
    }

    [Fact]
    public void New_sounds_get_no_shortcut_in_profiles_that_never_saw_them()
    {
        var c = Config();
        ProfileService.EnsureDefaults(c);
        c.Sounds.Add(new SoundEntry { Id = "new", Name = "New", Hotkey = "Num9" });
        ProfileService.Switch(c, "Streaming");
        Assert.Null(c.Sounds.Single(s => s.Id == "new").Hotkey);
        ProfileService.Switch(c, "General use");
        Assert.Equal("Num9", c.Sounds.Single(s => s.Id == "new").Hotkey); // kept where it was set
    }

    [Fact]
    public void Profiles_survive_save_and_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bb-prof-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigStore(Path.Combine(dir, "config.json"));
        var c = Config();
        ProfileService.EnsureDefaults(c);
        c.Channels.App.Gain = 33;
        ProfileService.Switch(c, "Streaming");
        store.Save(c);
        var loaded = store.Load();
        Assert.Equal("Streaming", loaded.ActiveProfile);
        Assert.Equal(33, loaded.Profiles.Single(p => p.Name == "General use").Channels.App.Gain);
        Assert.Equal(SoundRoute.MicOnly, loaded.Profiles[0].SoundRoutes["horn"]);
        Directory.Delete(dir, true);
    }
}
