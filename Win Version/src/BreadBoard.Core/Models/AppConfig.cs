namespace BreadBoard.Core.Models;

/// <summary>OneShot stacks a new voice per press; Loop/Toggle stop on the next press; Hold plays while held.</summary>
public enum PlayMode { OneShot, Toggle, Loop, Hold }

/// <summary>Where a sound plays: virtual mic and your headphones, only the virtual mic (others hear it, you don't), or only your headphones (a private preview).</summary>
public enum SoundRoute { Both, MicOnly, MonitorOnly }

public sealed class SoundEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Absolute path of the audio file in the user's library.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Website the file was downloaded from, if any.</summary>
    public string? SourceUrl { get; set; }
    public const string DefaultCategory = "General";
    public string Category { get; set; } = DefaultCategory;
    public string? Hotkey { get; set; }
    public int Volume { get; set; } = 85; // 0–100
    public PlayMode Mode { get; set; } = PlayMode.OneShot;
    public bool Favorite { get; set; }
    public string Icon { get; set; } = "volume";
    public string Tint { get; set; } = "beige";
    public SoundRoute Route { get; set; } = SoundRoute.Both;
    /// <summary>For a sequence: the sounds it plays one after another (no file of its own).</summary>
    public List<string>? Sequence { get; set; }
}

public sealed class ChannelSettings
{
    public const int MaxGain = 200; // above 100 % boosts: useful for quiet mics
    public int Gain { get; set; } = 80;
    public bool Muted { get; set; }
    public bool ToVirtualMic { get; set; } = true;
    public bool ToMonitor { get; set; }
}

public sealed class ChannelSet
{
    public ChannelSettings Mic { get; set; } = new() { Gain = 80 };
    /// <summary>Audio captured from one running application.</summary>
    public ChannelSettings App { get; set; } = new() { Gain = 65 };
    public ChannelSettings Soundboard { get; set; } = new() { Gain = 70, ToMonitor = true };
    public ChannelSettings Master { get; set; } = new() { Gain = 90 };
}

public sealed class ToggleWithValue
{
    public bool Enabled { get; set; }
    public int Value { get; set; }
}

public sealed class PushToTalkSettings
{
    public bool Enabled { get; set; }
    public string? Key { get; set; } = "V";
}

public sealed class ProcessingSettings
{
    /// <summary>Threshold as % of full scale.</summary>
    public ToggleWithValue NoiseGate { get; set; } = new() { Enabled = true, Value = 1 };
    /// <summary>Amount 0–100.</summary>
    public ToggleWithValue Compressor { get; set; } = new() { Enabled = false, Value = 40 };
    /// <summary>Ceiling as % of full scale.</summary>
    public ToggleWithValue Limiter { get; set; } = new() { Enabled = true, Value = 92 };
    /// <summary>How much app audio is lowered while you talk, 0–100.</summary>
    public ToggleWithValue Ducking { get; set; } = new() { Enabled = false, Value = 50 };
    public PushToTalkSettings PushToTalk { get; set; } = new();
}

/// <summary>Process IDs change between runs, so the app source is remembered by executable name.</summary>
public sealed class AppSourceSelection
{
    public string ProcessName { get; set; } = "";
    /// <summary>Friendly name ("Brave Browser") shown while the app isn't running.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Executable path when known (for the icon).</summary>
    public string? ExePath { get; set; }
}

public sealed class DeviceSettings
{
    /// <summary>Endpoint IDs; null = system default (input/monitor) or auto-detected virtual cable (output).</summary>
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public bool MicEnabled { get; set; } = true;
    public bool VirtualMicEnabled { get; set; } = true;
    /// <summary>Plays the monitor mix (by default your soundboard) on <see cref="MonitorDeviceId"/> so you hear it.</summary>
    public bool MonitorEnabled { get; set; } = true;
    public AppSourceSelection? AppSource { get; set; }
    /// <summary>Audio buffer size in ms for outputs and captures: lower = less delay, higher = fewer dropouts.</summary>
    public int LatencyMs { get; set; } = DefaultLatencyMs;

    public const int DefaultLatencyMs = 20;
    public static readonly (int Ms, string Hint)[] LatencyChoices = [(10, "lowest delay"), (20, "recommended"), (40, ""), (80, "most stable")];
}

public sealed class PlaybackSettings
{
    public bool AllowStacking { get; set; } = true;
    public int MaxVoices { get; set; } = 8;
    /// <summary>Version 1 setting, moved to <see cref="DeviceSettings.MonitorEnabled"/> by the version 2 migration.</summary>
    public bool HearOnThisPc { get; set; } = true;
}

public sealed class GlobalHotkeys
{
    public string? StopAll { get; set; } = "Ctrl+Shift+S";
    public string? MuteMic { get; set; } = "Ctrl+Shift+M";
    public string? RandomSound { get; set; } = "Ctrl+Shift+R";
    public string? ToggleMini { get; set; } = "Ctrl+Shift+G";
}

/// <summary>The mini-player's quick list.</summary>
public enum MiniTab { Favourites, Recent }

/// <summary>Window placement and navigation state restored on the next launch.</summary>
public sealed class UiSettings
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public string LastPage { get; set; } = "Soundboard";

    public double? MiniLeft { get; set; }
    public double? MiniTop { get; set; }
    public double MiniWidth { get; set; } = 320;
    public double MiniHeight { get; set; } = 600;
    public bool MiniTopmost { get; set; } = true;
    public bool MiniOpenOnStart { get; set; }
    /// <summary>Mini-player opacity, 40–100 %.</summary>
    public int MiniOpacity { get; set; } = 100;
    public MiniTab MiniTab { get; set; } = MiniTab.Favourites;
}

public sealed class AppConfig
{
    public const int CurrentVersion = 2;
    public int Version { get; set; } = CurrentVersion;
    public UiSettings Ui { get; set; } = new();
    public List<SoundEntry> Sounds { get; set; } = [];
    public List<string> Categories { get; set; } = ["Memes", "Reactions", "Effects", "Music"];
    /// <summary>Most recently played sound IDs, newest first.</summary>
    public List<string> Recent { get; set; } = [];
    public ChannelSet Channels { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public DeviceSettings Devices { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
    public GlobalHotkeys Hotkeys { get; set; } = new();

    public List<Profile> Profiles { get; set; } = [];
    public string ActiveProfile { get; set; } = "General use";
}
