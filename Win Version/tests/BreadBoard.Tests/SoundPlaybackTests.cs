using BreadBoard.Audio;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

public class SoundPlaybackTests
{
    private static SoundClip Clip(string id, int frames, float value = 0.25f) => new(id, Enumerable.Repeat(value, frames * 2).ToArray());

    private static (SoundboardEngine Engine, float[] Buf) Setup(PlayMode mode, int frames = 100)
    {
        var e = new SoundboardEngine();
        e.Register("s", Clip("s", frames, 0.5f), mode, 100);
        return (e, new float[64]);
    }

    [Fact]
    public void OneShot_plays_once_then_ends()
    {
        var (e, buf) = Setup(PlayMode.OneShot, frames: 16); // 32 samples
        e.Trigger("s");
        e.Render(buf, new float[buf.Length]);
        Assert.Equal(0.5f, buf[0]);
        Assert.Equal(0f, buf[40]);
        Assert.Empty(e.Playing);
    }

    [Fact]
    public void OneShot_stacks_when_allowed()
    {
        var (e, buf) = Setup(PlayMode.OneShot);
        e.Trigger("s"); e.Trigger("s");
        e.Render(buf, new float[buf.Length]);
        Assert.Equal(1.0f, buf[0], 3);
        e.AllowStacking = false;
        e.StopAll(); e.Trigger("s"); e.Trigger("s");
        e.Render(buf, new float[buf.Length]);
        Assert.Equal(0.5f, buf[0], 3);
    }

    [Fact]
    public void Toggle_and_loop_stop_on_second_press()
    {
        foreach (var mode in new[] { PlayMode.Toggle, PlayMode.Loop })
        {
            var (e, buf) = Setup(mode);
            e.Trigger("s"); e.Render(buf, new float[buf.Length]);
            Assert.Single(e.Playing);
            e.Trigger("s"); e.Render(buf, new float[buf.Length]);
            Assert.Empty(e.Playing);
            Assert.All(buf, v => Assert.Equal(0f, v));
        }
    }

    [Fact]
    public void Loop_wraps_past_clip_end()
    {
        var (e, buf) = Setup(PlayMode.Loop, frames: 10); // 20 samples < 64
        e.Trigger("s");
        e.Render(buf, new float[buf.Length]);
        Assert.All(buf, v => Assert.Equal(0.5f, v));
    }

    [Fact]
    public void Hold_plays_until_key_up()
    {
        var (e, buf) = Setup(PlayMode.Hold, frames: 10);
        e.Trigger("s", keyDown: true); e.Render(buf, new float[buf.Length]);
        Assert.Single(e.Playing);
        e.Trigger("s", keyDown: false); e.Render(buf, new float[buf.Length]);
        Assert.Empty(e.Playing);
    }

    [Fact]
    public void Max_voices_drops_the_oldest()
    {
        var e = new SoundboardEngine { MaxVoices = 2 };
        e.Register("a", Clip("a", 100, 0.1f), PlayMode.OneShot, 100);
        e.Register("b", Clip("b", 100, 0.2f), PlayMode.OneShot, 100);
        e.Register("c", Clip("c", 100, 0.4f), PlayMode.OneShot, 100);
        e.Trigger("a"); e.Trigger("b"); e.Trigger("c");
        var buf = new float[8];
        e.Render(buf, new float[buf.Length]);
        Assert.Equal(0.6f, buf[0], 3);
        Assert.Equal(["b", "c"], e.Playing);
    }

    [Fact]
    public void Volume_scales_output()
    {
        var e = new SoundboardEngine();
        e.Register("s", Clip("s", 100, 0.5f), PlayMode.OneShot, 50);
        var buf = new float[8];
        e.Trigger("s"); e.Render(buf, new float[buf.Length]);
        Assert.Equal(0.25f, buf[0], 3);
    }


    [Fact]
    public void Decodes_wav_to_48k_stereo()
    {
        var path = TestWav.Write(Path.Combine(Path.GetTempPath(), "bb-dec-" + Guid.NewGuid().ToString("N") + ".wav"), seconds: 1, rate: 22050, channels: 1);
        try
        {
            var clip = SoundLoader.Load("x", path);
            Assert.InRange(clip.Duration.TotalSeconds, 0.97, 1.03);
            Assert.InRange(clip.Samples.Length, 0.97 * AudioFormat.SampleRate * 2, 1.03 * AudioFormat.SampleRate * 2); // resampled to 48 kHz stereo
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
        e.Render(buf, new float[buf.Length]);
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
            if (i % 10 == 0) e.Render(buf, new float[buf.Length]);
        }
        e.Render(buf, new float[buf.Length]);
        Assert.InRange(buf[0], 0.079f, 0.081f); // exactly 8 voices of 0.01
        e.StopAll();
        e.Render(buf, new float[buf.Length]);
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
        e.Render(buf, new float[buf.Length]); e.Render(buf, new float[buf.Length]);
        Assert.InRange(e.GetProgress("a"), 0.09, 0.11); // 200 of 2000 samples recorded at the start of the 2nd block
        e.Stop("a");
        e.Render(buf, new float[buf.Length]);
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
        e.Render(buf, new float[buf.Length]);
        Assert.Equal(0.25f, buf[0], 3);
        e.Trigger("a"); e.Render(buf, new float[buf.Length]); // toggle: second press stops
        Assert.Empty(e.Playing);
    }

    [Fact]
    public void Unregistered_sound_is_ignored()
    {
        var e = new SoundboardEngine();
        e.Trigger("nope");
        var buf = new float[8];
        e.Render(buf, new float[buf.Length]);
        Assert.Empty(e.Playing);
    }
}
