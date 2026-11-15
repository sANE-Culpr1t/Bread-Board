using BreadBoard.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BreadBoard.Audio.Output;

/// <summary>Shared-mode, event-driven WASAPI playback of a 48 kHz float provider to one endpoint.</summary>
public sealed class OutputDevice : IDisposable
{
    private readonly WasapiPlayer _player;
    private int _disposed;

    private OutputDevice(WasapiPlayer player, string name)
    {
        _player = player;
        Name = name;
        _player.PlaybackStopped += (_, e) =>
        {
            if (Volatile.Read(ref _disposed) == 1) return;
            if (e.Exception is not null) Log.Warn($"Output '{Name}' stopped", e.Exception);
            Stopped?.Invoke(e.Exception);
        };
    }

    public string Name { get; }
    public event Action<Exception?>? Stopped;

    public static OutputDevice Start(MMDevice device, ISampleProvider source, int latencyMs = 20)
    {
        var player = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(latencyMs)
            .WithMmcssThreadPriority("Pro Audio")
            .Build();

        // Shared mode needs the device mix rate; resample only when the endpoint isn't 48 kHz.
        IWaveProvider provider = source.ToWaveProvider();
        if (!player.IsFormatSupported(AudioFormat.Wave))
        {
            var mix = player.DeviceMixFormat;
            Log.Info($"Output '{device.FriendlyName}' mix format {mix}; resampling");
            ISampleProvider p = source;
            if (mix.SampleRate != AudioFormat.SampleRate) p = new WdlResamplingSampleProvider(p, mix.SampleRate);
            provider = p.ToWaveProvider();
        }

        var output = new OutputDevice(player, device.FriendlyName);
        try
        {
            player.Init(provider);
            player.Play();
        }
        catch
        {
            output.Dispose();
            throw;
        }
        Log.Info($"Output started: {device.FriendlyName} ({player.OutputWaveFormat}, {player.LatencyMilliseconds} ms)");
        return output;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _player.Stop(); } catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        _player.Dispose();
        Log.Info($"Output stopped: {Name}");
    }
}
