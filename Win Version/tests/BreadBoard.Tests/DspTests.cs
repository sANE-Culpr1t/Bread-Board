using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Mixing;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

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
