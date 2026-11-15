using BreadBoard.Audio.Sources;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.App.Services;

/// <summary>App Audio: listing running apps and choosing which one joins the mix.</summary>
public sealed partial class SoundboardController
{
    /// <summary>Running apps that can be captured (worker thread; enumerating windows and sessions takes a moment).</summary>
    public Task<IReadOnlyList<AudioApp>> ListAppsAsync()
    {
        var devices = _audio.Value.Devices;
        return Task.Run(() => AudioAppEnumerator.List(devices));
    }

    /// <summary>Chooses the app whose audio joins the mix, or none. Remembered by executable name.</summary>
    public async Task SelectAppAsync(AudioApp? app)
    {
        _config.Update(c => c.Devices.AppSource = app is null ? null
            : new AppSourceSelection { ProcessName = app.ProcessName, DisplayName = app.Label, ExePath = app.ExePath });
        Log.Info(app is null ? "App audio: none" : $"App audio: {app.ProcessName} (pid {app.ProcessId})");
        await RestartAudioAsync();
    }
}
