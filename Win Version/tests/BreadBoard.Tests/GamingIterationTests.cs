using BreadBoard.Audio;
using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Mixing;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Config;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.Tests;

public class SoundRoutingTests
{
    private static SoundClip Clip(string id, float v = 0.5f) => new(id, Enumerable.Repeat(v, 2000).ToArray());

    [Theory]
    [InlineData(SoundRoute.Both, 0.5f, 0.5f)]
    [InlineData(SoundRoute.MicOnly, 0.5f, 0f)]
    [InlineData(SoundRoute.MonitorOnly, 0f, 0.5f)]
    public void Each_sound_goes_to_its_buses(SoundRoute route, float main, float monitor)
    {
        var e = new SoundboardEngine();
        e.Register("s", Clip("s"), PlayMode.Loop, 100, route);
        e.Trigger("s");
        var m = new float[64]; var h = new float[64];
        e.Render(m, h);
        Assert.Equal(main, m[10], 3);
        Assert.Equal(monitor, h[10], 3);
    }

    [Fact]
    public void Route_can_change_while_playing_and_single_bus_render_includes_everything()
    {
        var e = new SoundboardEngine();
        e.Register("s", Clip("s"), PlayMode.Loop, 100, SoundRoute.MonitorOnly);
        e.Trigger("s");
        var m = new float[64]; var h = new float[64];
        e.Render(m, h);
        Assert.Equal(0f, m[10]);
        e.Update("s", PlayMode.Loop, 100, SoundRoute.MicOnly);
        e.Render(m, h);
        Assert.Equal(0.5f, m[10], 3);
        Assert.Equal(0f, h[10]);
        var all = new float[64];
        e.Update("s", PlayMode.Loop, 100, SoundRoute.MonitorOnly);
        e.Render(all); // legacy single-bus render: routing ignored
        Assert.Equal(0.5f, all[10], 3);
    }

    [Fact]
    public void Mixer_keeps_headphones_only_sounds_off_the_virtual_mic()
    {
        var sb = new SoundboardEngine();
        sb.Register("private", Clip("private"), PlayMode.Loop, 100, SoundRoute.MonitorOnly);
        var mix = new MixerEngine(sb);
        mix.Board.Gain = 1; mix.BoardMonitor.Gain = 1; mix.Master.Gain = 1; mix.MasterLimiter.Enabled = false;
        mix.BoardRouting.ToMonitor = true;
        var ring = new FloatRingBuffer(4800);
        mix.MonitorOut = ring;
        sb.Trigger("private");
        var buf = new float[256];
        mix.Read(buf); mix.Read(buf);
        Assert.All(buf, v => Assert.Equal(0f, v));        // nothing on the virtual mic
        var mon = new float[256];
        ring.Skip(ring.Count - 256);
        ring.Read(mon);
        Assert.Equal(0.5f, mon[^1], 3);                    // but you hear it
        Assert.True(mix.BoardMonitor.TakePeak() > 0.4f);
    }
}

public class SequenceTests
{
    [Fact]
    public void Joins_clips_with_their_volumes_and_a_gap()
    {
        var a = new SoundClip("a", Enumerable.Repeat(1f, 100).ToArray());
        var b = new SoundClip("b", Enumerable.Repeat(1f, 100).ToArray());
        var seq = SequenceBuilder.Build("seq", [(a, 1f), (b, 0.5f)], gapMs: 10);
        var gap = AudioFormat.SamplesFor(10);
        Assert.Equal(200 + gap, seq.Samples.Length);
        Assert.Equal(1f, seq.Samples[0]);
        Assert.Equal(0f, seq.Samples[100 + gap / 2]);
        Assert.Equal(0.5f, seq.Samples[^1]);
    }

    [Fact]
    public void Is_capped_at_the_maximum_length()
    {
        var max = (int)(SoundLoader.MaxLength.TotalSeconds * AudioFormat.SampleRate * AudioFormat.Channels);
        var big = new SoundClip("big", new float[max - 10]);
        var seq = SequenceBuilder.Build("seq", [(big, 1f), (big, 1f)]);
        Assert.Equal(max, seq.Samples.Length);
    }
}

public sealed class LibrarySequenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bb-seq-" + Guid.NewGuid().ToString("N"));
    private readonly ConfigService _config;
    private readonly SoundLibrary _lib;

    public LibrarySequenceTests()
    {
        _config = new ConfigService(new ConfigStore(Path.Combine(_dir, "config.json")), debounceMs: 50);
        _lib = new SoundLibrary(_config, Path.Combine(_dir, "Sounds"));
    }

    private SoundEntry Sound(string name) => _lib.Import(TestWav.Write(Path.Combine(_dir, "src", name + ".wav")));

    [Fact]
    public void Sequences_are_named_validated_and_follow_their_members()
    {
        var a = Sound("alpha"); var b = Sound("beta"); var c = Sound("gamma");
        var seq = _lib.AddSequence([a.Id, b.Id, c.Id]);
        Assert.Equal("alpha → beta → gamma", seq.Name);
        Assert.False(SoundLibrary.FileMissing(seq)); // no file of its own
        Assert.Throws<ArgumentException>(() => _lib.AddSequence([a.Id]));
        Assert.Throws<ArgumentException>(() => _lib.AddSequence([seq.Id, a.Id])); // no nesting
        Assert.Single(_lib.SequencesUsing(b.Id));

        _lib.Remove(b.Id);
        Assert.Equal([a.Id, c.Id], _lib.Find(seq.Id)!.Sequence);
        _lib.Remove(c.Id);
        Assert.Null(_lib.Find(seq.Id)); // one sound left: no longer a sequence
        Assert.True(File.Exists(a.FilePath));
    }

    public void Dispose()
    {
        _config.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

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
