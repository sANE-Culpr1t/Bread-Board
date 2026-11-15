using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Audio.Sources;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

/// <summary>Real devices: monitor-only fallback, latency changes, feedback guard (needs VB-Cable; silent on speakers).</summary>
[Trait("Category", "Hardware")]
public sealed class DeviceRoutingTests
{
    private static double Rms(CaptureSource src)
    {
        var buf = new float[src.Buffer.Count];
        var n = src.Buffer.Read(buf);
        if (n == 0) return 0;
        double sum = 0;
        for (var i = 0; i < n; i++) sum += buf[i] * buf[i];
        return Math.Sqrt(sum / n);
    }

    private static SoundClip Tone() =>
        new("tone", Enumerable.Range(0, 48000 * 4).Select(i => 0.4f * MathF.Sin(2 * MathF.PI * 440 * (i / 2) / 48000f)).ToArray());

    private static async Task<double> HeardOnCable(AudioHost host, CaptureSource cable)
    {
        host.Soundboard.StopAll();
        host.Soundboard.Trigger("tone");
        await Task.Delay(400);
        cable.Buffer.Skip(cable.Buffer.Count);
        await Task.Delay(500);
        return Rms(cable);
    }

    [Fact]
    public async Task Monitor_only_fallback_latency_change_and_back_to_virtual_mic()
    {
        using var devices = new DeviceService();
        var cableOut = devices.FindVirtualCableOutput();
        var cableIn = devices.GetDevices(DeviceKind.Input).FirstOrDefault(d => d.IsVirtualCable);
        if (cableOut is null || cableIn is null) return;
        using var micSide = devices.Resolve(DeviceKind.Input, cableIn.Id)!;
        using var cable = CaptureSource.StartDevice(micSide);

        var cfg = new AppConfig();
        cfg.Devices.MicEnabled = false;
        cfg.Devices.OutputDeviceId = "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}"; // a device that doesn't exist
        cfg.Devices.MonitorEnabled = true;
        cfg.Devices.MonitorDeviceId = cableOut.Id; // monitor into the cable so the test is silent
        cfg.Channels.Soundboard.Gain = 100; cfg.Channels.Soundboard.ToMonitor = true;
        await using var host = new AudioHost();
        host.Soundboard.Register("tone", Tone(), PlayMode.Loop, 100);
        await host.StartAsync(cfg);

        // 1. No virtual-mic output: the monitor device drives the mix.
        Assert.NotEqual(SourceState.Running, host.Status.Output.State);
        Assert.Equal(SourceState.Running, host.Status.Monitor.State);
        Assert.True(host.MonitorOnly);
        Assert.True(await HeardOnCable(host, cable) > 0.05, "monitor-only mix not heard");

        // 2. Buffer size change reopens the stream and keeps working.
        cfg.Devices.LatencyMs = 40;
        await host.StartAsync(cfg);
        Assert.Equal(SourceState.Running, host.Status.Monitor.State);
        Assert.True(await HeardOnCable(host, cable) > 0.05, "not heard after latency change");

        // 3. Back to the normal path: virtual cable output, monitor off.
        cfg.Devices.OutputDeviceId = null;
        cfg.Devices.MonitorEnabled = false;
        await host.StartAsync(cfg);
        Assert.Equal(SourceState.Running, host.Status.Output.State);
        Assert.False(host.MonitorOnly);
        Assert.True(await HeardOnCable(host, cable) > 0.05, "virtual mic path not heard");

        // 4. Feedback guard: the cable's own capture side is refused as the microphone.
        cfg.Devices.MicEnabled = true;
        cfg.Devices.InputDeviceId = cableIn.Id;
        await host.StartAsync(cfg);
        Assert.Equal(SourceState.Failed, host.Status.Mic.State);
        Assert.Contains("Feedback", host.Status.Mic.Error, StringComparison.Ordinal);
        host.Soundboard.StopAll();
    }
}
