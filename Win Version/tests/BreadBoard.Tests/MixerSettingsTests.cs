using BreadBoard.Audio;
using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Mixing;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

public class MixerSettingsTests
{
    [Fact]
    public void Monitor_tap_plays_the_monitor_mix_when_there_is_no_main_output()
    {
        var sb = new SoundboardEngine();
        sb.Register("s", new SoundClip("s", Enumerable.Repeat(0.5f, 9600).ToArray()), PlayMode.Loop, 100);
        var mix = new MixerEngine(sb);
        mix.Board.Gain = 1f; mix.Master.Gain = 1f;
        mix.BoardRouting.ToMonitor = true;
        var ring = new FloatRingBuffer(AudioFormat.SamplesFor(500));
        mix.MonitorOut = ring;
        var tap = new MonitorTap(mix, ring);
        sb.Trigger("s");
        var buf = new float[480];
        tap.Read(buf); tap.Read(buf);
        Assert.Equal(0.5f, buf[^1], 3);

        mix.BoardRouting.ToMonitor = false; // monitor routing still respected
        tap.Read(buf); tap.Read(buf);
        Assert.Equal(0f, buf[^1], 3);
    }

    [Fact]
    public async Task Saved_settings_shape_what_the_virtual_mic_sends()
    {
        await using var host = new AudioHost();
        host.Soundboard.Register("s", new SoundClip("s", Enumerable.Repeat(0.8f, 96_000).ToArray()), PlayMode.Loop, 100);
        host.Soundboard.Trigger("s");
        var buf = new float[480];
        float Level() { for (var i = 0; i < 20; i++) host.Mixer.Read(buf); return buf.Max(MathF.Abs); }

        var cfg = new AppConfig();
        cfg.Channels.Soundboard.Gain = 50; cfg.Channels.Master.Gain = 100;
        cfg.Processing.Limiter.Enabled = false;
        host.ApplyMixSettings(cfg);
        Assert.Equal(0.4f, Level(), 2);

        cfg.Channels.Soundboard.Gain = 100;
        cfg.Processing.Limiter.Enabled = true; cfg.Processing.Limiter.Value = 50;
        host.ApplyMixSettings(cfg);
        Assert.InRange(Level(), 0.3f, 0.5f);

        cfg.Channels.Master.Muted = true;
        host.ApplyMixSettings(cfg);
        Assert.Equal(0f, Level());

        cfg.Channels.App.ToMonitor = true; // you already hear the app: it is never monitored twice
        host.ApplyMixSettings(cfg);
        Assert.False(host.Mixer.AppRouting.ToMonitor);
    }
}