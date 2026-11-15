using System.Runtime.InteropServices;
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Mixing;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Audio.Sources;
using BreadBoard.Core.Config;
using BreadBoard.Core.Models;
using BreadBoard.Platform.Hotkeys;

namespace BreadBoard.Tests;

public class ConfigMigrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void V1_hear_on_this_pc_becomes_monitor(bool hear, bool expectedMonitor)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bb-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        var h = hear ? "true" : "false";
        var m = expectedMonitor ? "false" : "true"; // start opposite so the migration must change it
        File.WriteAllText(path, "{\"version\":1,\"playback\":{\"hearOnThisPc\":" + h + "},\"devices\":{\"monitorEnabled\":" + m + ",\"latencyMs\":33}}");
        var cfg = new ConfigStore(path).Load();
        Assert.Equal(expectedMonitor, cfg.Devices.MonitorEnabled);
        Assert.Equal(20, cfg.Devices.LatencyMs); // unsupported value reset
        Assert.Equal(AppConfig.CurrentVersion, cfg.Version);
        Directory.Delete(dir, true);
    }
}

public class MixerSettingsTests
{
    [Fact]
    public void Monitor_tap_plays_the_monitor_mix_when_there_is_no_main_output()
    {
        var sb = new SoundboardEngine();
        sb.Register("s", new SoundClip("s", Enumerable.Repeat(0.5f, 9600).ToArray()), PlayMode.Loop, 100);
        var mix = new MixerEngine(sb);
        mix.Board.Gain = 1f; mix.Master.Gain = 1f;
        mix.BoardRouting.ToMonitor = true;
        var ring = new FloatRingBuffer(AudioFormat.SamplesFor(500));
        mix.MonitorOut = ring;
        var tap = new MonitorTap(mix, ring);
        sb.Trigger("s");
        var buf = new float[480];
        tap.Read(buf); tap.Read(buf);
        Assert.Equal(0.5f, buf[^1], 3);

        mix.BoardRouting.ToMonitor = false; // monitor routing still respected
        tap.Read(buf); tap.Read(buf);
        Assert.Equal(0f, buf[^1], 3);
    }

    [Fact]
    public async Task Settings_reach_the_mixer()
    {
        var cfg = new AppConfig();
        cfg.Channels.Mic.Gain = 150; cfg.Channels.Mic.Muted = true; cfg.Channels.Mic.ToMonitor = true;
        cfg.Channels.App.ToVirtualMic = false;
        cfg.Processing.NoiseGate.Value = 7;
        cfg.Processing.Compressor.Enabled = true; cfg.Processing.Compressor.Value = 60;
        cfg.Processing.Limiter.Value = 80;
        cfg.Processing.Ducking.Enabled = true;
        cfg.Processing.PushToTalk.Enabled = true;
        cfg.Devices.VirtualMicEnabled = false;
        await using var host = new AudioHost();
        host.ApplyMixSettings(cfg);
        var m = host.Mixer;
        Assert.Equal(1.5f, m.Mic.Gain, 3);
        Assert.True(m.Mic.Muted);
        Assert.True(m.MicRouting.ToMonitor);
        Assert.False(m.AppRouting.ToMain);
        Assert.Equal(0.07f, m.Gate.Threshold, 3);
        Assert.True(m.MicCompressor.Enabled);
        Assert.Equal(0.6f, m.MicCompressor.Amount, 3);
        Assert.Equal(0.8f, m.MasterLimiter.Ceiling, 3);
        Assert.True(m.AppDucker.Enabled);
        Assert.True(m.PushToTalkEnabled);
        Assert.False(m.MasterRouting.ToMain);
    }
}

/// <summary>Real devices: monitor-only fallback, latency changes, feedback guard (needs VB-Cable; silent on speakers).</summary>
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

public class PushToTalkKeyTests
{
    [Fact]
    public async Task Watches_a_key_without_taking_it_from_other_apps()
    {
        using var first = new GlobalHotkeyService();
        using var second = new GlobalHotkeyService(); // stands in for any other app listening to the key
        var events1 = new List<bool>();
        var events2 = new List<bool>();
        var done = new TaskCompletionSource();
        first.WatchKey("F19", d => { lock (events1) events1.Add(d); });
        second.WatchKey("F19", d => { lock (events2) { events2.Add(d); if (events2.Count == 2) done.TrySetResult(); } });
        await Task.Delay(200);

        Send(0x82, false); Send(0x82, false); // auto-repeat: reported once
        await Task.Delay(50);
        Send(0x82, true);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        lock (events1) Assert.Equal([true, false], events1);
        lock (events2) Assert.Equal([true, false], events2); // the key passed through the first hook
        first.WatchKey(null, null);
    }

    private static void Send(ushort vk, bool up)
    {
        var input = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } };
        Assert.Equal(1u, SendInput(1, [input], Marshal.SizeOf<INPUT>()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
