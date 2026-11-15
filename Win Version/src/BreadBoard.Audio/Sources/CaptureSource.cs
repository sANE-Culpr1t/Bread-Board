using System.Runtime.InteropServices;
using BreadBoard.Audio.Dsp;
using BreadBoard.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BreadBoard.Audio.Sources;

/// <summary>
/// A live input (microphone, or one application's audio via process loopback) delivered as
/// 48 kHz stereo float into a ring buffer that the mixer drains. Windows converts the format.
/// </summary>
public sealed class CaptureSource : IDisposable
{
    private readonly WasapiRecorder _recorder;
    private int _disposed;

    private CaptureSource(WasapiRecorder recorder, string name)
    {
        _recorder = recorder;
        Name = name;
        _recorder.DataAvailable += OnData;
        _recorder.RecordingStopped += (_, e) =>
        {
            if (Volatile.Read(ref _disposed) == 1) return;
            if (e.Exception is not null) Log.Warn($"Capture '{Name}' stopped", e.Exception);
            Stopped?.Invoke(e.Exception);
        };
    }

    public string Name { get; }
    /// <summary>Holds up to ~1 s; the mixer trims it to keep latency low.</summary>
    public FloatRingBuffer Buffer { get; } = new(AudioFormat.SamplesFor(1000));
    /// <summary>Raised (on a capture thread) when capture ends; non-null exception = failure such as device removal.</summary>
    public event Action<Exception?>? Stopped;

    public static CaptureSource StartDevice(MMDevice device, int bufferMs = 20)
    {
        var rec = new WasapiRecorderBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithFormat(AudioFormat.Wave)
            .WithBufferLength(bufferMs)
            .WithMmcssThreadPriority("Pro Audio")
            .Build();
        return Start(rec, device.FriendlyName);
    }

    /// <summary>Captures everything <paramref name="processId"/> and its child processes play (Windows 10 2004+).</summary>
    public static async Task<CaptureSource> StartProcessAsync(int processId, string name, int bufferMs = 20)
    {
        var rec = await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)processId, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithFormat(AudioFormat.Wave)
            .WithBufferLength(bufferMs)
            .WithMmcssThreadPriority("Pro Audio")
            .BuildAsync()
            .ConfigureAwait(false);
        return Start(rec, name);
    }

    private static CaptureSource Start(WasapiRecorder rec, string name)
    {
        var src = new CaptureSource(rec, name);
        try
        {
            rec.StartRecording();
        }
        catch
        {
            src.Dispose();
            throw;
        }
        Log.Info($"Capture started: {name} ({rec.WaveFormat})");
        return src;
    }

    private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if ((flags & AudioClientBufferFlags.Silent) != 0)
            Buffer.WriteSilence(data.Length / sizeof(float));
        else
            Buffer.Write(MemoryMarshal.Cast<byte, float>(data));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _recorder.StopRecording(); } catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        _recorder.Dispose();
        Log.Info($"Capture stopped: {Name}");
    }
}
