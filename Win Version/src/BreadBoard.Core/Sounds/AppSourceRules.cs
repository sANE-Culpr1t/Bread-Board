namespace BreadBoard.Core.Sounds;

/// <summary>Which apps make sense as the "App Audio" source, and which deserve a warning.</summary>
public static class AppSourceRules
{
    private static readonly Dictionary<string, string> VoiceChat = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discord"] = "Discord", ["discordptb"] = "Discord", ["discordcanary"] = "Discord",
        ["teams"] = "Teams", ["ms-teams"] = "Teams", ["zoom"] = "Zoom", ["skype"] = "Skype",
        ["slack"] = "Slack", ["ts3client_win64"] = "TeamSpeak", ["teamspeak"] = "TeamSpeak",
        ["mumble"] = "Mumble", ["guilded"] = "Guilded", ["whatsapp"] = "WhatsApp", ["telegram"] = "Telegram",
    };

    /// <summary>Null if <paramref name="processName"/> may be captured; otherwise why not.</summary>
    public static string? Blocked(string processName)
    {
        var name = Normalize(processName);
        if (name.Equals("breadboard", StringComparison.OrdinalIgnoreCase))
            return "Bread Board can't capture itself; it would echo forever.";
        if (name.Equals("audiodg", StringComparison.OrdinalIgnoreCase) || name.StartsWith("voicemeeter", StringComparison.OrdinalIgnoreCase))
            return "That's part of the audio system; capturing it would include everything, your own sounds too.";
        return null;
    }

    /// <summary>Warning shown before capturing an app that plays other people's voices.</summary>
    public static string? Warning(string processName) =>
        VoiceChat.TryGetValue(Normalize(processName), out var app)
            ? $"{app} plays other people's voices. Capturing it sends them back into your mic, so they'd hear themselves."
            : null;

    private static string Normalize(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
