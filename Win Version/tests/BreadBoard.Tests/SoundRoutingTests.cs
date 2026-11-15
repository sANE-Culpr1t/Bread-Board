using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Mixing;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Models;

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
    public void Route_can_change_while_playing()
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
