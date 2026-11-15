using NAudio.Wave;

namespace BreadBoard.Audio;

/// <summary>The single internal pipeline format: 48 kHz, stereo, 32-bit float, interleaved.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public static readonly WaveFormat Wave = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    /// <summary>Interleaved samples in <paramref name="ms"/> milliseconds.</summary>
    public static int SamplesFor(int ms) => SampleRate * Channels * ms / 1000;
}
