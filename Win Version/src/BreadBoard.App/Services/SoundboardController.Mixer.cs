using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.App.Services;

public enum MixChannel { Mic, App, Soundboard, Master }

public enum Processor { NoiseGate, Compressor, Limiter, Ducking }

/// <summary>Mixer, devices, processing, mutes and push-to-talk (Iteration 4).</summary>
public sealed partial class SoundboardController
{
    private bool _metersVisible = true;

    public float MicLevel { get; private set; }
    /// <summary>No virtual-mic output: the monitor device plays the mix so you still hear it.</summary>
    public bool MonitorOnlyMode => _host.AudioCreated && _host.Audio.MonitorOnly;
    public float MasterLevel { get; private set; }
    public HostStatus Status { get; private set; } = new(new(SourceState.Off, null), new(SourceState.Off, null), new(SourceState.Off, null), new(SourceState.Off, null));
    /// <summary>Push-to-talk key is held (true) or released (false).</summary>
    public event Action<bool>? PushToTalkChanged;
    /// <summary>Levels, mutes, routing or processing changed (cheap UI refresh).</summary>
    public event Action? MixChanged;
    /// <summary>Audio devices were plugged in, removed or changed.</summary>
    public event Action? DevicesListChanged;

    internal void RaiseDevicesListChanged() { if (!_disposed) DevicesListChanged?.Invoke(); }
    public bool PushToTalkDown { get; private set; }

    /// <summary>Meters stop updating while the main window is minimised (saves CPU).</summary>
    public bool MetersVisible
    {
        get => _metersVisible;
        set { _metersVisible = value; UpdateTicking(); }
    }

    public static ChannelSettings Settings(AppConfig c, MixChannel ch) => ch switch
    {
        MixChannel.Mic => c.Channels.Mic,
        MixChannel.App => c.Channels.App,
        MixChannel.Soundboard => c.Channels.Soundboard,
        _ => c.Channels.Master,
    };

    public static ToggleWithValue Settings(AppConfig c, Processor p) => p switch
    {
        Processor.NoiseGate => c.Processing.NoiseGate,
        Processor.Compressor => c.Processing.Compressor,
        Processor.Limiter => c.Processing.Limiter,
        _ => c.Processing.Ducking,
    };

    /// <summary>Changes a channel's gain (0–200 %), mute or routing; applied to the running mix at once.</summary>
    public void SetChannel(MixChannel ch, int? gain = null, bool? muted = null, bool? toVirtualMic = null, bool? toMonitor = null)
    {
        _host.Config.Update(c =>
        {
            var s = Settings(c, ch);
            if (gain is { } g) s.Gain = Math.Clamp(g, 0, 200);
            if (muted is { } m) s.Muted = m;
            if (toVirtualMic is { } v) s.ToVirtualMic = v;
            if (toMonitor is { } mon) s.ToMonitor = mon;
        });
        ApplyMix();
        MixChanged?.Invoke();
    }

    public void SetProcessing(Processor p, bool? enabled = null, int? value = null)
    {
        _host.Config.Update(c =>
        {
            var s = Settings(c, p);
            if (enabled is { } e) s.Enabled = e;
            if (value is { } v) s.Value = Math.Clamp(v, 0, 100);
        });
        ApplyMix();
        MixChanged?.Invoke();
    }

    public void ToggleMicMute()
    {
        var muted = !Config.Channels.Mic.Muted;
        SetChannel(MixChannel.Mic, muted: muted);
        Notice?.Invoke(muted ? "Microphone muted" : "Microphone on", false);
    }

    public void ToggleMasterMute() => SetChannel(MixChannel.Master, muted: !Config.Channels.Master.Muted);

    /// <summary>Turns the virtual mic on/off (off = silence is sent, meters keep working).</summary>
    public void SetVirtualMic(bool on)
    {
        _host.Config.Update(c => c.Devices.VirtualMicEnabled = on);
        ApplyMix();
        MixChanged?.Invoke();
    }

    /// <summary>Device choices, mic/monitor on-off and buffer size: reopens only what changed.</summary>
    public async Task SetDevicesAsync(Action<DeviceSettings> change)
    {
        _host.Config.Update(c => change(c.Devices));
        if (Config.Devices.MicEnabled || _engineStart is not null) await EnsureEngineAsync();
        if (_engineStart is null) return;
        await _engineStart;
        await _host.Audio.StartAsync(AudioConfig());
        UpdateEngineSummary();
    }

    public IReadOnlyList<AudioDeviceInfo> ListDevices(DeviceKind kind) => _disposed ? [] : _host.Audio.Devices.GetDevices(kind);

    public async Task SetPushToTalkAsync(bool enabled)
    {
        _host.Config.Update(c => c.Processing.PushToTalk.Enabled = enabled);
        ApplyMix();
        await RegisterHotkeysAsync();
        MixChanged?.Invoke();
    }

    private void ApplyMix()
    {
        if (EngineStarted) _host.Audio.ApplyMixSettings(AudioConfig());
    }

    /// <summary>Push-to-talk watches its key without blocking it (the key still works in games and chat).</summary>
    private void ApplyPushToTalkWatch(AppConfig cfg)
    {
        var ptt = cfg.Processing.PushToTalk;
        var key = Hotkey.TryParse(ptt.Key, out var hk) ? hk.Key : null;
        if (ptt.Enabled && key is not null)
        {
            _host.Hotkeys.WatchKey(key, down =>
            {
                if (_host.AudioCreated) _host.Audio.Mixer.PushToTalkDown = down;
                _ui.BeginInvoke(() => { PushToTalkDown = down; PushToTalkChanged?.Invoke(down); });
            });
            Log.Info($"Push-to-talk on {key}");
        }
        else _host.Hotkeys.WatchKey(null, null);
        _hotkeys["global:ptt"] = new("global:ptt", "Push-to-talk", key, null);
    }
}
