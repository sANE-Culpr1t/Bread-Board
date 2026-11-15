using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Mixing;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

public class RingBufferTests
{
    [Fact]
    public void Wraps_and_preserves_order()
    {
        var rb = new FloatRingBuffer(8);
        var outBuf = new float[8];
        for (var round = 0; round < 5; round++)
        {
            Assert.Equal(6, rb.Write([1, 2, 3, 4, 5, 6]));
            Assert.Equal(6, rb.Read(outBuf));
            Assert.Equal([1f, 2, 3, 4, 5, 6], outBuf[..6]);
        }
    }

    [Fact]
    public void Drops_excess_when_full_and_skip_discards()
    {
        var rb = new FloatRingBuffer(4);
        Assert.Equal(4, rb.Write([1, 2, 3, 4, 5]));
        rb.Skip(2);
        var o = new float[4];
        Assert.Equal(2, rb.Read(o));
        Assert.Equal([3f, 4], o[..2]);
    }
}

public class SoundboardTests
{
    private static SoundClip Clip(string id, int frames) => new(id, Enumerable.Repeat(0.5f, frames * 2).ToArray());

    private static (SoundboardEngine Engine, float[] Buf) Setup(PlayMode mode, int frames = 100)
    {
        var e = new SoundboardEngine();
        e.Register("s", Clip("s", frames), mode, 100);
        return (e, new float[64]);
    }

    [Fact]
    public void OneShot_plays_once_then_ends()
    {
        var (e, buf) = Setup(PlayMode.OneShot, frames: 16); // 32 samples
        e.Trigger("s");
        e.Render(buf);
        Assert.Equal(0.5f, buf[0]);
        Assert.Equal(0f, buf[40]);
        Assert.Empty(e.Playing);
    }

    [Fact]
    public void OneShot_stacks_when_allowed()
    {
        var (e, buf) = Setup(PlayMode.OneShot);
        e.Trigger("s"); e.Trigger("s");
        e.Render(buf);
        Assert.Equal(1.0f, buf[0], 3);
        e.AllowStacking = false;
        e.StopAll(); e.Trigger("s"); e.Trigger("s");
        e.Render(buf);
        Assert.Equal(0.5f, buf[0], 3);
    }

    [Fact]
    public void Toggle_and_loop_stop_on_second_press()
    {
        foreach (var mode in new[] { PlayMode.Toggle, PlayMode.Loop })
        {
            var (e, buf) = Setup(mode);
            e.Trigger("s"); e.Render(buf);
            Assert.Single(e.Playing);
            e.Trigger("s"); e.Render(buf);
            Assert.Empty(e.Playing);
            Assert.All(buf, v => Assert.Equal(0f, v));
        }
    }

    [Fact]
    public void Loop_wraps_past_clip_end()
    {
        var (e, buf) = Setup(PlayMode.Loop, frames: 10); // 20 samples < 64
        e.Trigger("s");
        e.Render(buf);
        Assert.All(buf, v => Assert.Equal(0.5f, v));
    }

    [Fact]
    public void Hold_plays_until_key_up()
    {
        var (e, buf) = Setup(PlayMode.Hold, frames: 10);
        e.Trigger("s", keyDown: true); e.Render(buf);
        Assert.Single(e.Playing);
        e.Trigger("s", keyDown: false); e.Render(buf);
        Assert.Empty(e.Playing);
    }

    [Fact]
    public void Max_voices_drops_oldest()
    {
        var (e, buf) = Setup(PlayMode.OneShot);
        e.MaxVoices = 2;
        e.Trigger("s"); e.Trigger("s"); e.Trigger("s");
        e.Render(buf);
        Assert.Equal(1.0f, buf[0], 3);
    }

    [Fact]
    public void Volume_scales_output()
    {
        var e = new SoundboardEngine();
        e.Register("s", Clip("s", 100), PlayMode.OneShot, 50);
        var buf = new float[8];
        e.Trigger("s"); e.Render(buf);
        Assert.Equal(0.25f, buf[0], 3);
    }
}

public class DspTests
{
    [Fact]
    public void Strip_ramps_to_gain_and_mutes()
    {
        var strip = new ChannelStrip { Gain = 0.5f };
        var buf = Enumerable.Repeat(1f, 256).ToArray();
        strip.Process(buf); // ramp from 1 → 0.5
        Array.Fill(buf, 1f);
        strip.Process(buf);
        Assert.All(buf, v => Assert.Equal(0.5f, v, 3));
        strip.Muted = true;
        Array.Fill(buf, 1f); strip.Process(buf);
        Array.Fill(buf, 1f); strip.Process(buf);
        Assert.All(buf, v => Assert.Equal(0f, v, 3));
        Assert.True(strip.TakePeak() >= 0f);
    }

    [Fact]
    public void Limiter_never_exceeds_ceiling()
    {
        var lim = new Limiter { Enabled = true, Ceiling = 0.8f };
        var buf = Enumerable.Range(0, 4800).Select(i => 1.5f * MathF.Sin(i * 0.05f)).ToArray();
        lim.Process(buf);
        Assert.All(buf, v => Assert.InRange(v, -0.8001f, 0.8001f));
    }

    [Fact]
    public void Gate_silences_noise_and_passes_speech()
    {
        var gate = new NoiseGate { Enabled = true, Threshold = 0.1f };
        var noise = Enumerable.Repeat(0.01f, AudioFormat.SamplesFor(400)).ToArray();
        gate.Process(noise);
        Assert.True(MathF.Abs(noise[^1]) < 0.001f);
        var speech = Enumerable.Repeat(0.5f, AudioFormat.SamplesFor(100)).ToArray();
        gate.Process(speech);
        Assert.True(speech[^1] > 0.45f);
    }

    [Fact]
    public void Mixer_respects_routing_and_virtual_mic_switch()
    {
        var sb = new SoundboardEngine();
        sb.Register("s", new SoundClip("s", Enumerable.Repeat(0.5f, 2000).ToArray()), PlayMode.Loop, 100);
        var mix = new MixerEngine(sb);
        mix.Board.Gain = 1f; mix.Master.Gain = 1f; mix.MasterLimiter.Enabled = false;
        sb.Trigger("s");
        var buf = new float[512];
        mix.Read(buf); mix.Read(buf);
        Assert.Equal(0.5f, buf[^1], 3);

        mix.BoardRouting.ToMain = false;
        mix.Read(buf);
        Assert.Equal(0f, buf[^1], 3);

        mix.BoardRouting.ToMain = true;
        mix.MasterRouting.ToMain = false; // virtual mic off
        mix.Read(buf);
        Assert.All(buf, v => Assert.Equal(0f, v));
    }

    [Theory]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)", "Speakers (VB-Audio Virtual Cable)", true)]
    [InlineData("Microphone (USB)", "Speakers (VB-Audio Virtual Cable)", false)]
    public void Detects_feedback_loop(string input, string output, bool expected) =>
        Assert.Equal(expected, DeviceService.IsFeedbackLoop(input, output));
}
