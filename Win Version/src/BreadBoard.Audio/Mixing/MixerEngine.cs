using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Audio.Sources;
using NAudio.Wave;

namespace BreadBoard.Audio.Mixing;

public sealed class ChannelRouting
{
    private volatile bool _toMain = true, _toMonitor;
    public bool ToMain { get => _toMain; set => _toMain = value; }
    public bool ToMonitor { get => _toMonitor; set => _toMonitor = value; }
}

public readonly record struct MeterLevels(float Mic, float App, float Soundboard, float Master);

/// <summary>
/// Combines microphone, app audio and soundboard into the virtual-mic output (and an optional
/// monitor mix). Runs on the output device's render thread; it never allocates in steady state.
/// </summary>
public sealed class MixerEngine : ISampleProvider
{
    // Keep live inputs ~40 ms behind real time; anything beyond 120 ms is dropped (clock drift, stalls).
    private static readonly int TargetLatency = AudioFormat.SamplesFor(40);
    private static readonly int MaxLatency = AudioFormat.SamplesFor(120);

    private float[] _mic = [], _app = [], _sb = [], _mon = [];
    private volatile CaptureSource? _micSource, _appSource;
    private volatile FloatRingBuffer? _monitorOut;
    private volatile bool _pttEnabled, _pttDown;

    public MixerEngine(SoundboardEngine soundboard) => Soundboard = soundboard;

    public WaveFormat WaveFormat => AudioFormat.Wave;
    public SoundboardEngine Soundboard { get; }

    public ChannelStrip Mic { get; } = new();
    public ChannelStrip App { get; } = new();
    public ChannelStrip Board { get; } = new();
    public ChannelStrip Master { get; } = new();
    public ChannelRouting MicRouting { get; } = new() { ToMain = true };
    public ChannelRouting AppRouting { get; } = new() { ToMain = true };
    public ChannelRouting BoardRouting { get; } = new() { ToMain = true, ToMonitor = true };
    public ChannelRouting MasterRouting { get; } = new() { ToMain = true };

    public NoiseGate Gate { get; } = new();
    public Compressor MicCompressor { get; } = new();
    public Ducker AppDucker { get; } = new();
    public Limiter MasterLimiter { get; } = new();

    public bool PushToTalkEnabled { get => _pttEnabled; set => _pttEnabled = value; }
    /// <summary>Set by the hotkey layer while the push-to-talk key is held.</summary>
    public bool PushToTalkDown { get => _pttDown; set => _pttDown = value; }

    public CaptureSource? MicSource { get => _micSource; set => _micSource = value; }
    public CaptureSource? AppSource { get => _appSource; set => _appSource = value; }
    /// <summary>Ring buffer drained by the monitor (headphones) output, or null when monitoring is off.</summary>
    public FloatRingBuffer? MonitorOut { get => _monitorOut; set => _monitorOut = value; }

    public MeterLevels TakeLevels() => new(Mic.TakePeak(), App.TakePeak(), Board.TakePeak(), Master.TakePeak());

    public int Read(Span<float> buffer)
    {
        var output = buffer;
        var count = output.Length;
        Ensure(ref _mic, count); Ensure(ref _app, count); Ensure(ref _sb, count); Ensure(ref _mon, count);
        var mic = _mic.AsSpan(0, count);
        var app = _app.AsSpan(0, count);
        var sb = _sb.AsSpan(0, count);

        Pull(_micSource, mic);
        Pull(_appSource, app);
        Soundboard.Render(sb);

        // Microphone chain: gate → push-to-talk → compressor → fader.
        Gate.Process(mic);
        if (_pttEnabled && !_pttDown) mic.Clear();
        MicCompressor.Process(mic);
        Mic.Process(mic);

        AppDucker.Process(app, mic);
        App.Process(app);
        Board.Process(sb);

        output.Clear();
        if (MicRouting.ToMain) Add(output, mic);
        if (AppRouting.ToMain) Add(output, app);
        if (BoardRouting.ToMain) Add(output, sb);
        Master.Process(output);
        MasterLimiter.Process(output);

        if (_monitorOut is { } mon)
        {
            var m = _mon.AsSpan(0, count);
            m.Clear();
            if (MicRouting.ToMonitor) Add(m, mic);
            if (AppRouting.ToMonitor) Add(m, app);
            if (BoardRouting.ToMonitor) Add(m, sb);
            for (var i = 0; i < m.Length; i++) m[i] = Math.Clamp(m[i], -1f, 1f);
            if (mon.Count > MaxLatency) mon.Skip(mon.Count - TargetLatency);
            mon.Write(m);
        }

        if (!MasterRouting.ToMain) output.Clear(); // virtual mic switched off: send silence, keep meters alive
        return count;
    }

    private static void Pull(CaptureSource? src, Span<float> dest)
    {
        if (src is null) { dest.Clear(); return; }
        var ring = src.Buffer;
        if (ring.Count > MaxLatency) ring.Skip(ring.Count - TargetLatency);
        var n = ring.Read(dest);
        dest[n..].Clear(); // underrun: pad with silence
    }

    private static void Add(Span<float> dst, ReadOnlySpan<float> src)
    {
        for (var i = 0; i < dst.Length; i++) dst[i] += src[i];
    }

    private static void Ensure(ref float[] buf, int n)
    {
        if (buf.Length < n) buf = new float[n]; // only grows when the device asks for a larger block
    }
}

/// <summary>Plays a ring buffer (the monitor mix), padding with silence when empty.</summary>
public sealed class RingBufferSampleProvider(FloatRingBuffer ring) : ISampleProvider
{
    public WaveFormat WaveFormat => AudioFormat.Wave;

    public int Read(Span<float> buffer)
    {
        var n = ring.Read(buffer);
        buffer[n..].Clear();
        return buffer.Length;
    }
}
