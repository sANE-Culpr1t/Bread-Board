using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.Core.Config;

/// <summary>
/// Gaming / General use / Streaming / Custom profiles. The live settings always belong to the active
/// profile; switching first stores them into it, then applies the other one — so nothing is lost.
/// </summary>
public static class ProfileService
{
    public static readonly string[] Names = ["General use", "Gaming", "Streaming", "Custom"];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["General use"] = "Everyday calls and chat",
        ["Gaming"] = "In-game voice: shortcuts you can reach mid-game",
        ["Streaming"] = "Stream audio: app audio, ducking, careful levels",
        ["Custom"] = "Anything else",
    };

    /// <summary>Creates any missing default profiles from the current settings.</summary>
    public static void EnsureDefaults(AppConfig c)
    {
        foreach (var name in Names)
            if (!c.Profiles.Exists(p => p.Name == name)) c.Profiles.Add(Capture(c, name));
        if (!c.Profiles.Exists(p => p.Name == c.ActiveProfile)) c.ActiveProfile = Names[0];
    }

    /// <summary>A copy of the current settings as a profile.</summary>
    public static Profile Capture(AppConfig c, string name)
    {
        var copy = ConfigStore.Clone(c); // deep copy, so the profile never shares objects with live settings
        return new Profile
        {
            Name = name,
            Channels = copy.Channels,
            Processing = copy.Processing,
            Hotkeys = copy.Hotkeys,
            MicEnabled = c.Devices.MicEnabled,
            VirtualMicEnabled = c.Devices.VirtualMicEnabled,
            MonitorEnabled = c.Devices.MonitorEnabled,
            AppSource = copy.Devices.AppSource,
            SoundHotkeys = c.Sounds.ToDictionary(s => s.Id, s => s.Hotkey),
            SoundRoutes = c.Sounds.ToDictionary(s => s.Id, s => s.Route),
        };
    }

    /// <summary>Applies <paramref name="p"/> to the live settings. Sounds the profile doesn't know keep no shortcut.</summary>
    public static void Apply(AppConfig c, Profile p)
    {
        var copy = Clone(p);
        c.Channels = copy.Channels;
        c.Processing = copy.Processing;
        c.Hotkeys = copy.Hotkeys;
        c.Devices.MicEnabled = p.MicEnabled;
        c.Devices.VirtualMicEnabled = p.VirtualMicEnabled;
        c.Devices.MonitorEnabled = p.MonitorEnabled;
        c.Devices.AppSource = copy.AppSource;
        foreach (var s in c.Sounds)
        {
            s.Hotkey = p.SoundHotkeys.GetValueOrDefault(s.Id);
            s.Route = p.SoundRoutes.GetValueOrDefault(s.Id, SoundRoute.Both);
        }
    }

    /// <summary>Stores the live settings in the active profile and switches to <paramref name="name"/>.</summary>
    public static void Switch(AppConfig c, string name)
    {
        EnsureDefaults(c);
        var target = c.Profiles.Find(p => p.Name == name) ?? throw new KeyNotFoundException(name);
        var index = c.Profiles.FindIndex(p => p.Name == c.ActiveProfile);
        if (index >= 0) c.Profiles[index] = Capture(c, c.ActiveProfile);
        Apply(c, target);
        c.ActiveProfile = name;
        Log.Info($"Profile switched to {name}");
    }

    private static Profile Clone(Profile p)
    {
        var holder = new AppConfig { Profiles = [p] };
        return ConfigStore.Clone(holder).Profiles[0];
    }
}
