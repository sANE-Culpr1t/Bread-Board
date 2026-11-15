using BreadBoard.Core.Models;

namespace BreadBoard.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>A parsed shortcut such as "Ctrl+Shift+S" or "Num5". Same text format as the prototype.</summary>
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

public sealed record HotkeyOwner(string Key, string Label, string Combo);

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

    public static IReadOnlyList<HotkeyOwner> Owners(AppConfig cfg)
    {
        var list = new List<HotkeyOwner>();
        foreach (var s in cfg.Sounds)
            if (Norm(s.Hotkey) is { } c) list.Add(new($"sound:{s.Id}", s.Name, c));
        if (Norm(cfg.Hotkeys.StopAll) is { } a) list.Add(new("global:stopAll", "Stop all sounds", a));
        if (Norm(cfg.Hotkeys.MuteMic) is { } b) list.Add(new("global:muteMic", "Mute microphone", b));
        if (Norm(cfg.Hotkeys.ToggleMini) is { } m) list.Add(new("global:toggleMini", "Toggle mini-player", m));
        if (Norm(cfg.Hotkeys.RandomSound) is { } r) list.Add(new("global:random", "Play a random sound", r));
        if (Norm(cfg.Processing.PushToTalk.Key) is { } p) list.Add(new("global:ptt", "Push-to-talk", p));
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

    public static IReadOnlySet<string> Duplicates(AppConfig cfg) =>
        Owners(cfg).GroupBy(o => o.Combo).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

    private static string? Norm(string? combo) => Hotkey.TryParse(combo, out var hk) ? hk.ToString() : null;
}
