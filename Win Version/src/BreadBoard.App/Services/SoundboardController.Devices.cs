using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Sources;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Services;

/// <summary>
/// The Bread Board mic as Windows sees it: named "Bread Board Mic", shown only while Bread Board runs,
/// optionally Windows' default microphone, and its level. Also the chosen app's own volume.
/// </summary>
public sealed partial class SoundboardController
{
    /// <summary>Windows' default microphone is the Bread Board mic, so apps on "Default" hear Bread Board.</summary>
    public bool BreadBoardIsWindowsMic
    {
        get
        {
            if (_disposed || !_audio.IsValueCreated) return false;
            try
            {
                var mic = _audio.Value.Devices.FindVirtualCableInput();
                return mic is not null && mic.Id == _audio.Value.Devices.DefaultId(DeviceKind.Input);
            }
            catch (System.Runtime.InteropServices.COMException) { return false; }
        }
    }

    /// <summary>Called as the engine starts: show and name the Bread Board mic, and make it the default if chosen.</summary>
    private void ClaimBreadBoardMic(DeviceService devices)
    {
        try
        {
            devices.SetBreadBoardDevicesEnabled(true);
            if (!devices.NameBreadBoardDevices()) Log.Warn("Windows refused to rename the Bread Board mic");
            if (Config.Devices.UseAsWindowsMic) MakeWindowsMic(devices);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            Log.Warn("Setting up the Bread Board mic failed", ex);
        }
    }

    /// <summary>
    /// On quit: give Windows its own default microphone back and hide the Bread Board mic from every app's list.
    /// Runs after the engine has stopped; uses its own device service.
    /// </summary>
    public static void ReleaseBreadBoardMic(Core.Config.ConfigService config)
    {
        try
        {
            using var devices = new DeviceService();
            if (devices.FindVirtualCableInput() is { } mic && devices.DefaultId(DeviceKind.Input) == mic.Id && RealMic(devices, config.Current) is { } back)
                DeviceService.SetDefaultInput(back.Id);
            devices.SetBreadBoardDevicesEnabled(false);
            Log.Info("Bread Board mic hidden until the next start");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            Log.Warn("Hiding the Bread Board mic failed", ex);
        }
    }

    /// <summary>Makes the Bread Board mic Windows' default microphone while Bread Board runs, or gives the default back.</summary>
    public async Task UseAsWindowsMicAsync(bool on)
    {
        await EnsureEngineAsync();
        var devices = _audio.Value.Devices;
        try
        {
            if (on)
            {
                if (devices.FindVirtualCableInput() is null) { Notice?.Invoke("Install VB-CABLE first (Mixer › Set up the virtual mic).", true); return; }
                _config.Update(c => c.Devices.UseAsWindowsMic = true);
                MakeWindowsMic(devices);
                Notice?.Invoke("Apps using the default microphone now hear Bread Board.", false);
            }
            else
            {
                _config.Update(c => c.Devices.UseAsWindowsMic = false);
                if (RealMic(devices, Config) is not { } back) { Notice?.Invoke("No other microphone to switch back to.", true); return; }
                DeviceService.SetDefaultInput(back.Id);
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

    private void MakeWindowsMic(DeviceService devices)
    {
        if (devices.FindVirtualCableInput() is not { } mic) return;
        var before = devices.DefaultId(DeviceKind.Input);
        if (before == mic.Id) return;
        _config.Update(c =>
        {
            c.Devices.WindowsMicBefore = before;
            c.Devices.InputDeviceId ??= before; // "system default" would now be Bread Board itself
        });
        DeviceService.SetDefaultInput(mic.Id);
    }

    /// <summary>The microphone Bread Board's mic replaced, else the one Bread Board listens to, else any real one.</summary>
    private static AudioDeviceInfo? RealMic(DeviceService devices, Core.Models.AppConfig c)
    {
        var real = devices.GetDevices(DeviceKind.Input).Where(d => !d.IsVirtualCable).ToList();
        return real.Find(d => d.Id == c.Devices.WindowsMicBefore) ?? real.Find(d => d.Id == c.Devices.InputDeviceId) ?? real.FirstOrDefault();
    }

    /// <summary>Level of the Bread Board mic as other apps receive it (its Windows device volume), 0–100; null when there's no mic.</summary>
    public int? BreadBoardMicLevel
    {
        get
        {
            if (_disposed || !_audio.IsValueCreated || _audio.Value.Devices.FindVirtualCableInput() is not { } mic) return null;
            return _audio.Value.Devices.GetVolume(mic.Id) is { } v ? (int)Math.Round(v * 100) : null;
        }
    }

    public bool LoopbackTestRunning => _audio.IsValueCreated && _audio.Value.LoopbackTestRunning;

    /// <summary>Starts or stops playing what others hear (the Bread Board mic) in your headphones.</summary>
    public async Task SetLoopbackTestAsync(bool on)
    {
        await EnsureEngineAsync();
        if (!on) _audio.Value.StopLoopbackTest();
        else if (_audio.Value.StartLoopbackTest() is { } why) Notice?.Invoke($"Test couldn't start: {why}", true);
        else Notice?.Invoke("Playing what others hear in your headphones. Talk or play a sound.", false);
        MixChanged?.Invoke();
    }

    public void SetBreadBoardMicLevel(int percent)
    {
        if (!_audio.IsValueCreated || _audio.Value.Devices.FindVirtualCableInput() is not { } mic) return;
        _audio.Value.Devices.SetVolume(mic.Id, percent / 100f);
    }

    /// <summary>The chosen app's own volume (what you hear, like Windows' Volume mixer), 0–100; null when it has no sound session.</summary>
    public int? AppVolume => Config.Devices.AppSource is { } sel && _audio.IsValueCreated
        ? AudioAppEnumerator.GetVolume(_audio.Value.Devices, sel.ProcessName) is { } v ? (int)Math.Round(v * 100) : null
        : null;

    public void SetAppVolume(int percent)
    {
        if (Config.Devices.AppSource is { } sel && _audio.IsValueCreated)
            AudioAppEnumerator.SetVolume(_audio.Value.Devices, sel.ProcessName, percent / 100f);
    }
}
