using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.App.Services;

public enum MixChannel { Mic, App, Soundboard, Master }

public enum Processor { NoiseGate, Compressor, Limiter, Ducking }

/// <summary>Mixer, devices, processing, mutes and push-to-talk.</summary>
public sealed partial class SoundboardController
{
    private bool _metersVisible = true;

    public HostStatus Status { get; private set; } = HostStatus.Off;
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

    /// <summary>App audio is never monitored: you already hear the app itself (enforced by the engine).</summary>
    public static bool CanMonitor(MixChannel ch) => ch is MixChannel.Mic or MixChannel.Soundboard;

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
        _config.Update(c =>
        {
            var s = Settings(c, ch);
            if (gain is { } g) s.Gain = Math.Clamp(g, 0, ChannelSettings.MaxGain);
            if (muted is { } m) s.Muted = m;
            if (toVirtualMic is { } v) s.ToVirtualMic = v;
            if (toMonitor is { } mon) s.ToMonitor = mon;
        });
        ApplyMix();
        MixChanged?.Invoke();
    }

    public void SetProcessing(Processor p, bool? enabled = null, int? value = null)
    {
        _config.Update(c =>
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

    /// <summary>Device choices, mic/monitor on-off and buffer size: reopens only what changed.</summary>
    public async Task SetDevicesAsync(Action<DeviceSettings> change)
    {
        _config.Update(c => change(c.Devices));
        await RestartAudioAsync();
    }

    /// <summary>No virtual audio cable is installed, so nothing can hear Bread Board as a microphone.</summary>
    public bool VirtualCableMissing => !_disposed && _audio.IsValueCreated && !ListDevices(DeviceKind.Output).Any(d => d.IsVirtualCable);

    /// <summary>Windows' default microphone is Bread Board's (the virtual cable), so apps on "Default" hear Bread Board.</summary>
    public bool BreadBoardIsWindowsMic
    {
        get
        {
            if (_disposed || !_audio.IsValueCreated) return false;
            try
            {
                var cable = _audio.Value.Devices.FindVirtualCableInput();
                return cable is not null && cable.Id == _audio.Value.Devices.DefaultId(DeviceKind.Input);
            }
            catch (System.Runtime.InteropServices.COMException) { return false; }
        }
    }

    /// <summary>
    /// Makes Bread Board's mic Windows' default microphone, or gives the default back to the one it replaced.
    /// Bread Board keeps listening to your real mic either way (it never records its own output).
    /// </summary>
    public async Task UseAsWindowsMicAsync(bool on)
    {
        await EnsureEngineAsync();
        var devices = _audio.Value.Devices;
        try
        {
            if (on)
            {
                if (devices.FindVirtualCableInput() is not { } cable) { Notice?.Invoke("Install VB-CABLE first (Mixer › Set up the virtual mic).", true); return; }
                var before = devices.DefaultId(DeviceKind.Input);
                if (before == cable.Id) return;
                _config.Update(c =>
                {
                    c.Devices.WindowsMicBefore = before;
                    c.Devices.InputDeviceId ??= before; // "system default" would now be Bread Board itself
                });
                DeviceService.SetDefaultInput(cable.Id);
                Notice?.Invoke("Apps using the default microphone now hear Bread Board.", false);
            }
            else
            {
                var real = devices.GetDevices(DeviceKind.Input).Where(d => !d.IsVirtualCable).ToList();
                var back = real.Find(d => d.Id == Config.Devices.WindowsMicBefore) ?? real.Find(d => d.Id == Config.Devices.InputDeviceId) ?? real.FirstOrDefault();
                if (back is null) { Notice?.Invoke("No other microphone to switch back to.", true); return; }
                DeviceService.SetDefaultInput(back.Id);
                _config.Update(c => c.Devices.WindowsMicBefore = null);
                Notice?.Invoke($"Windows' default microphone is {back.Name} again.", false);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            Log.Warn("Changing the Windows default microphone failed", ex);
            Notice?.Invoke("Windows didn't allow changing the default microphone. Set it in Sound settings › Input instead.", true);
        }
        await RestartAudioAsync();
        MixChanged?.Invoke();
    }

    public IReadOnlyList<AudioDeviceInfo> ListDevices(DeviceKind kind)
    {
        if (_disposed) return [];
        try { return _audio.Value.Devices.GetDevices(kind); }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Log.Warn($"Listing {kind} devices failed", ex);
            return [];
        }
    }

    public async Task SetPushToTalkAsync(bool enabled)
    {
        _config.Update(c => c.Processing.PushToTalk.Enabled = enabled);
        ApplyMix();
        await RegisterHotkeysAsync();
        MixChanged?.Invoke();
    }

    private void ApplyMix()
    {
        if (EngineStarted) _audio.Value.ApplyMixSettings(AudioConfig());
    }

    /// <summary>Push-to-talk watches its key without blocking it (the key still works in games and chat).</summary>
    private void ApplyPushToTalkWatch(AppConfig cfg)
    {
        var ptt = cfg.Processing.PushToTalk;
        var key = Hotkey.TryParse(ptt.Key, out var hk) ? hk.Key : null;
        if (ptt.Enabled && key is not null)
        {
            _hotkeyService.Value.WatchKey(key, down =>
            {
                if (_audio.IsValueCreated) _audio.Value.Mixer.PushToTalkDown = down;
                _ui.BeginInvoke(() => { PushToTalkDown = down; PushToTalkChanged?.Invoke(down); });
            });
            Log.Info($"Push-to-talk on {key}");
        }
        else _hotkeyService.Value.WatchKey(null, null);
        _hotkeys[HotkeyRules.PushToTalk] = new(HotkeyRules.PushToTalk, HotkeyRules.Global(HotkeyRules.PushToTalk).Label, key, null);
    }
}
