using BreadBoard.Audio;
using BreadBoard.Audio.Soundboard;

namespace BreadBoard.Tests;

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
        const int max = 3 * 60 * 48_000 * 2; // 3 minutes of 48 kHz stereo
        var big = new SoundClip("big", new float[max - 10]);
        var seq = SequenceBuilder.Build("seq", [(big, 1f), (big, 1f)]);
        Assert.Equal(max, seq.Samples.Length);
    }
}
