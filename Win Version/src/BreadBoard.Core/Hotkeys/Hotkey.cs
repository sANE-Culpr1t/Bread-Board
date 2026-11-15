using BreadBoard.Core.Models;

namespace BreadBoard.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>A parsed shortcut such as "Ctrl+Shift+S" or "Num5".</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var mods = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "meta": mods |= HotkeyModifiers.Win; break;
                default: return false;
            }
        }
        var key = parts[^1];
        if (key.Length == 0) return false;
        hotkey = new Hotkey(mods, key.Length == 1 ? key.ToUpperInvariant() : key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}

internal sealed record HotkeyOwner(string Key, string Label, string Combo);

/// <summary>An app-wide shortcut and where its combination is saved.</summary>
public sealed record GlobalShortcut(string Key, string Label, Func<AppConfig, string?> Get, Action<AppConfig, string?> Set);

/// <summary>Reserved-shortcut and duplicate rules shared by the UI and hotkey registration.</summary>
public static class HotkeyRules
{
    private static readonly Dictionary<string, string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alt+F4"] = "Windows uses Alt+F4 to close the active window",
        ["Alt+Tab"] = "Windows uses Alt+Tab for the window switcher",
        ["Alt+Esc"] = "Windows uses Alt+Esc to cycle windows",
        ["Ctrl+Esc"] = "Windows uses Ctrl+Esc to open Start",
        ["Ctrl+Alt+Delete"] = "Reserved by Windows security",
        ["Ctrl+Shift+Esc"] = "Windows uses this for Task Manager",
    };

    public static string? ReservedReason(Hotkey hk)
    {
        var text = hk.ToString();
        if (Reserved.TryGetValue(text, out var why)) return why;
        return hk.Modifiers.HasFlag(HotkeyModifiers.Win) ? "Windows reserves most Win-key shortcuts" : null;
    }

    public const string StopAll = "global:stopAll", ToggleMini = "global:toggleMini", RandomSound = "global:random",
        MuteMic = "global:muteMic", PushToTalk = "global:ptt";

    /// <summary>Every app-wide shortcut, in the order the Shortcuts page lists them.</summary>
    public static readonly GlobalShortcut[] Globals =
    [
        new(StopAll, "Stop all sounds", c => c.Hotkeys.StopAll, (c, v) => c.Hotkeys.StopAll = v),
        new(ToggleMini, "Toggle mini-player", c => c.Hotkeys.ToggleMini, (c, v) => c.Hotkeys.ToggleMini = v),
        new(RandomSound, "Play a random sound", c => c.Hotkeys.RandomSound, (c, v) => c.Hotkeys.RandomSound = v),
        new(MuteMic, "Mute microphone", c => c.Hotkeys.MuteMic, (c, v) => c.Hotkeys.MuteMic = v),
        new(PushToTalk, "Push-to-talk", c => c.Processing.PushToTalk.Key, (c, v) => c.Processing.PushToTalk.Key = v),
    ];

    public static GlobalShortcut Global(string key) =>
        Array.Find(Globals, g => g.Key == key) ?? throw new ArgumentOutOfRangeException(nameof(key), key);

    public static string SoundOwner(string soundId) => "sound:" + soundId;

    public static bool TrySoundId(string ownerKey, out string soundId)
    {
        var ok = ownerKey.StartsWith("sound:", StringComparison.Ordinal);
        soundId = ok ? ownerKey["sound:".Length..] : "";
        return ok;
    }

    private static List<HotkeyOwner> Owners(AppConfig cfg)
    {
        var list = new List<HotkeyOwner>();
        foreach (var s in cfg.Sounds)
            if (Norm(s.Hotkey) is { } c) list.Add(new(SoundOwner(s.Id), s.Name, c));
        foreach (var g in Globals)
            if (Norm(g.Get(cfg)) is { } c) list.Add(new(g.Key, g.Label, c));
        return list;
    }

    /// <summary>Null when <paramref name="combo"/> may be assigned to <paramref name="ownerKey"/>; otherwise why not.</summary>
    public static string? Validate(AppConfig cfg, string combo, string ownerKey)
    {
        if (!Hotkey.TryParse(combo, out var hk)) return $"“{combo}” is not a valid shortcut.";
        if (ReservedReason(hk) is { } why) return $"{hk} can't be used: {why}.";
        var text = hk.ToString();
        var clash = Owners(cfg).FirstOrDefault(o => o.Combo == text && o.Key != ownerKey);
        return clash is null ? null : $"{text} is already used by “{clash.Label}”.";
    }

    private static string? Norm(string? combo) => Hotkey.TryParse(combo, out var hk) ? hk.ToString() : null;
}
