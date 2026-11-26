namespace BreadBoard.Core.Models;

/// <summary>
/// A saved set of sound shortcuts, mixer levels, processing, routing and global shortcuts. The sound
/// library itself is shared; profiles only change how it's used.
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = "";
    public ChannelSet Channels { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public GlobalHotkeys Hotkeys { get; set; } = new();
    public bool MicEnabled { get; set; } = true;
    public bool MonitorEnabled { get; set; } = true;
    public AppSourceSelection? AppSource { get; set; }
    /// <summary>Sound ID → shortcut (null = none).</summary>
    public Dictionary<string, string?> SoundHotkeys { get; set; } = [];
    /// <summary>Sound ID → route.</summary>
    public Dictionary<string, SoundRoute> SoundRoutes { get; set; } = [];
}
