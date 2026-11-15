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
    public static TimeSpan MaxLength { get; set; } = TimeSpan.FromMinutes(3);

    public static SoundClip Load(string id, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Sound file not found", path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ogg" => LoadVorbis(id, path),
            ".wav" or ".mp3" => LoadMediaFoundation(id, path),
            _ => throw new NotSupportedException($"Unsupported audio format '{ext}'. Use WAV, MP3 or OGG."),
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

        var max = (int)(MaxLength.TotalSeconds * AudioFormat.SampleRate * AudioFormat.Channels);
        var buffer = new float[AudioFormat.SampleRate * AudioFormat.Channels]; // 0.5 s chunks
        var output = new List<float>(buffer.Length * 4);
        int n;
        while ((n = p.Read(buffer.AsSpan())) > 0)
        {
            output.AddRange(buffer.AsSpan(0, n));
            if (output.Count >= max) break;
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
