using System.Diagnostics;
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Sources;
using BreadBoard.Core.Models;
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

/// <summary>
/// Real process-loopback capture of a helper app (`bb tone`, which plays a tone into the virtual cable
/// so the speakers stay quiet): capture, level, app closing, and automatic reconnect when it restarts.
/// </summary>
public sealed class AppCaptureTests : IDisposable
{
    private readonly List<Process> _helpers = [];

    private static string BbExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BreadBoard.slnx"))) dir = dir.Parent;
        var cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";
        var exe = Path.Combine(dir!.FullName, "src", "BreadBoard.Cli", "bin", cfg, "net10.0-windows10.0.19041.0", "bb.exe");
        Assert.True(File.Exists(exe), $"CLI not built: {exe}");
        return exe;
    }

    private Process StartTone(double seconds)
    {
        var p = Process.Start(new ProcessStartInfo(BbExe(), $"tone --seconds {seconds}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        _helpers.Add(p);
        return p;
    }

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }

    /// <summary>Highest App-channel peak over a short window (the mixer runs on the output device clock).</summary>
    private static async Task<float> AppPeak(AudioHost host, int ms = 600)
    {
        host.Mixer.App.TakePeak();
        await Task.Delay(ms);
        return host.Mixer.App.TakePeak();
    }

    [Fact]
    public async Task Captures_an_app_follows_it_closing_and_reconnects_when_it_restarts()
    {
        using (var probe = new DeviceService())
            if (probe.FindVirtualCableOutput() is null) return; // needs VB-Cable (present on the dev PC)

        AudioHost.AppWaitMs = 400;
        var cfg = new AppConfig();
        cfg.Devices.MicEnabled = false;
        cfg.Devices.MonitorEnabled = false;
        cfg.Channels.App.Gain = 100;
        cfg.Devices.AppSource = new AppSourceSelection { ProcessName = "bb", DisplayName = "Tone helper" };
        await using var host = new AudioHost();

        // 1. App not running yet: the engine waits for it.
        await host.StartAsync(cfg);
        Assert.Equal(SourceState.Unavailable, host.Status.App.State);
        Assert.Contains("Waiting for", host.Status.App.Error, StringComparison.Ordinal);

        // 2. It starts: picked up automatically, and its sound arrives on the App channel.
        var first = StartTone(30);
        Assert.True(await Until(() => host.Status.App.State == SourceState.Running, 8000), $"not captured: {host.Status.App}");
        Assert.True(await Until(() => AppPeak(host).Result > 0.1f, 4000), "no app audio arrived");

        // 3. It closes: noticed through the process-exit signal.
        first.Kill();
        Assert.True(await Until(() => host.Status.App.State != SourceState.Running, 4000), "close not noticed");

        // 4. It comes back: captured again without user action.
        StartTone(30);
        Assert.True(await Until(() => host.Status.App.State == SourceState.Running, 8000), $"not re-captured: {host.Status.App}");
        Assert.True(await Until(() => AppPeak(host).Result > 0.1f, 4000), "no app audio after reconnect");

        // 5. Muting the App channel silences it in the mix; choosing no app stops capture.
        cfg.Channels.App.Muted = true;
        host.ApplyMixSettings(cfg);
        await Task.Delay(200);
        Assert.True(await AppPeak(host) < 0.01f);
        cfg.Devices.AppSource = null;
        await host.StartAsync(cfg);
        Assert.Equal(SourceState.Off, host.Status.App.State);
    }

    [Fact]
    public async Task Lists_a_playing_app_and_finds_it_by_name()
    {
        using var devices = new DeviceService();
        if (devices.FindVirtualCableOutput() is null) return;
        var helper = StartTone(15);
        await Task.Delay(1500); // let its audio session become active

        var apps = AudioAppEnumerator.List(devices);
        var bb = Assert.Single(apps, a => a.ProcessId == helper.Id);
        Assert.True(bb.HasAudioSession);
        Assert.True(bb.IsPlaying);
        Assert.EndsWith("bb.exe", bb.ExePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(apps, a => a.ProcessId == Environment.ProcessId);
        Assert.Equal(helper.Id, AudioAppEnumerator.Find(apps, "bb.exe")!.ProcessId);
        Assert.Equal(helper.Id, AudioAppEnumerator.FindRunning("bb")!.ProcessId);
        Assert.True(AudioAppEnumerator.IsRunning("bb"));
        Assert.False(AudioAppEnumerator.IsRunning("definitely-not-running-" + Guid.NewGuid().ToString("N")[..8]));
    }

    public void Dispose()
    {
        AudioHost.AppWaitMs = 3000;
        foreach (var p in _helpers)
        {
            try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
            p.Dispose();
        }
    }
}
