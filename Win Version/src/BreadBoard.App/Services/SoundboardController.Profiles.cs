using BreadBoard.Core.Config;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Services;

/// <summary>Profiles: switching stores the live settings in the active profile and applies the other one.</summary>
public sealed partial class SoundboardController
{
    /// <summary>The active profile changed (sound shortcuts, routes, mixer and devices may all differ).</summary>
    public event Action? ProfileChanged;

    public string ActiveProfile => Config.ActiveProfile;

    /// <summary>Saves the current settings into the active profile and applies <paramref name="name"/>.</summary>
    public async Task SwitchProfileAsync(string name)
    {
        if (name == Config.ActiveProfile) return;
        _config.Update(c => ProfileService.Switch(c, name));
        foreach (var e in Config.Sounds)
        {
            if (Get(e.Id) is not { } item) continue;
            item.Apply(e);
            if (EngineStarted) _audio.Value.Soundboard.Update(e.Id, e.Mode, e.Volume, e.Route);
        }
        await RestartAudioAsync();
        await RegisterHotkeysAsync();
        LibraryChanged?.Invoke();
        MixChanged?.Invoke();
        ProfileChanged?.Invoke();
        Notice?.Invoke($"Profile: {name}", false);
        Log.Info($"Profile {name} applied");
    }
}