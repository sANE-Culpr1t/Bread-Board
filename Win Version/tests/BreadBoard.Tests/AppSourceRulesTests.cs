using BreadBoard.Core.Sounds;

namespace BreadBoard.Tests;

public class AppSourceRulesTests
{
    [Theory]
    [InlineData("BreadBoard")]
    [InlineData("BreadBoard.exe")]
    [InlineData("audiodg")]
    [InlineData("voicemeeter8x64")]
    public void Blocks_self_and_system_audio(string name) => Assert.NotNull(AppSourceRules.Blocked(name));

    [Theory]
    [InlineData("brave")]
    [InlineData("Spotify.exe")]
    [InlineData("vlc")]
    public void Allows_normal_apps(string name)
    {
        Assert.Null(AppSourceRules.Blocked(name));
        Assert.Null(AppSourceRules.Warning(name));
    }

    [Theory]
    [InlineData("Discord", "Discord")]
    [InlineData("discord.exe", "Discord")]
    [InlineData("ms-teams", "Teams")]
    [InlineData("Zoom", "Zoom")]
    public void Warns_about_voice_chat_apps(string name, string app) =>
        Assert.StartsWith(app, AppSourceRules.Warning(name), StringComparison.Ordinal);
}
