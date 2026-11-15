using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.ViewModels;
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Import;
using BreadBoard.Audio.Sources;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.App.Services;

/// <summary>App Audio: listing running apps and choosing which one joins the mix.</summary>
public sealed partial class SoundboardController
{
    // ---- app audio ----------------------------------------------------------------------

    /// <summary>Running apps that can be captured (worker thread; enumerating windows and sessions takes a moment).</summary>
    public Task<IReadOnlyList<AudioApp>> ListAppsAsync()
    {
        var devices = _host.Audio.Devices;
        return Task.Run(() => AudioAppEnumerator.List(devices));
    }

    /// <summary>Chooses the app whose audio joins the mix, or none. Remembered by executable name.</summary>
    public async Task SelectAppAsync(AudioApp? app)
    {
        _host.Config.Update(c => c.Devices.AppSource = app is null ? null
            : new AppSourceSelection { ProcessName = app.ProcessName, WindowTitle = app.WindowTitle, DisplayName = app.Label, ExePath = app.ExePath });
        Log.Info(app is null ? "App audio: none" : $"App audio: {app.ProcessName} (pid {app.ProcessId})");
        if (app is null && _engineStart is null) return;
        await EnsureEngineAsync();
        await _host.Audio.StartAsync(AudioConfig());
        UpdateEngineSummary();
    }


}
