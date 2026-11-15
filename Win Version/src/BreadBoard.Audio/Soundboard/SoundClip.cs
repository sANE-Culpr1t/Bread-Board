using BreadBoard.Core.Sounds;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace BreadBoard.Audio.Soundboard;

/// <summary>A sound fully decoded to the pipeline format (48 kHz stereo float).</summary>
public sealed class SoundClip(string id, float[] samples)
{
    public string Id { get; } = id;
    public float[] Samples { get; } = samples;
    public int Frames => Samples.Length / AudioFormat.Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Frames / AudioFormat.SampleRate);
}

/// <summary>Decodes WAV/MP3 (Media Foundation) and OGG (NVorbis) once, at import/load time.</summary>
public static class SoundLoader
{
    /// <summary>Hard cap so a huge file can't exhaust memory (10 minutes ≈ 230 MB is too much; 3 min ≈ 70 MB).</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(3);
    public static readonly int MaxSamples = (int)(MaxLength.TotalSeconds * AudioFormat.SampleRate * AudioFormat.Channels);

    public static SoundClip Load(string id, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Sound file not found", path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ogg" => LoadVorbis(id, path),
            ".wav" or ".mp3" => LoadMediaFoundation(id, path),
            _ => throw new NotSupportedException($"Unsupported audio format '{ext}'. Use {AudioFiles.Description}."),
        };
    }

    private static SoundClip LoadMediaFoundation(string id, string path)
    {
        using var reader = new MediaFoundationReader(path);
        return Decode(id, reader.ToSampleProvider());
    }

    private static SoundClip LoadVorbis(string id, string path)
    {
        using var vorbis = new VorbisReader(path);
        return Decode(id, new VorbisSampleProvider(vorbis));
    }

    private static SoundClip Decode(string id, ISampleProvider source)
    {
        var p = source;
        if (p.WaveFormat.Channels == 1) p = new MonoToStereoSampleProvider(p);
        else if (p.WaveFormat.Channels > 2) p = new MultiplexingSampleProvider([p], 2);
        if (p.WaveFormat.SampleRate != AudioFormat.SampleRate) p = new WdlResamplingSampleProvider(p, AudioFormat.SampleRate);

        var buffer = new float[AudioFormat.SamplesFor(1000)];
        var output = new List<float>(buffer.Length * 4);
        int n;
        while ((n = p.Read(buffer.AsSpan())) > 0)
        {
            output.AddRange(buffer.AsSpan(0, n));
            if (output.Count >= MaxSamples) break;
        }
        if (output.Count % 2 != 0) output.RemoveAt(output.Count - 1);
        return new SoundClip(id, [.. output]);
    }

    private sealed class VorbisSampleProvider(VorbisReader reader) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate, reader.Channels);
        public int Read(Span<float> buffer) => reader.ReadSamples(buffer);
    }
}

/// <summary>Joins sounds into one clip (a "sequence"), each at its own volume, with a short gap between them.</summary>
public static class SequenceBuilder
{
    public static SoundClip Build(string id, IReadOnlyList<(SoundClip Clip, float Volume)> parts, int gapMs = 150)
    {
        var gap = AudioFormat.SamplesFor(gapMs);
        var total = parts.Sum(p => p.Clip.Samples.Length) + gap * Math.Max(0, parts.Count - 1);
        var data = new float[Math.Min(total, SoundLoader.MaxSamples)];
        var pos = 0;
        for (var i = 0; i < parts.Count && pos < data.Length; i++)
        {
            var (clip, vol) = parts[i];
            var n = Math.Min(clip.Samples.Length, data.Length - pos);
            for (var k = 0; k < n; k++) data[pos + k] = clip.Samples[k] * vol;
            pos += n + gap;
        }
        return new SoundClip(id, data);
    }
}