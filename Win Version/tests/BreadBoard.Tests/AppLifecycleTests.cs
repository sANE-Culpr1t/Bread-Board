using System.Diagnostics;
using System.Text.Json;
using BreadBoard.Core.Logging;

namespace BreadBoard.Tests;

/// <summary>Launches the real app (built Debug exe) with an isolated data folder.</summary>
[Trait("Category", "Hardware")]
public sealed class AppLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bb-app-" + Guid.NewGuid().ToString("N"));

    private static string AppExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BreadBoard.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var cfg = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";
        var exe = Path.Combine(dir!.FullName, "src", "BreadBoard.App", "bin", cfg, "net10.0-windows10.0.19041.0", "BreadBoard.exe");
        Assert.True(File.Exists(exe), $"App not built: {exe}");
        return exe;
    }

    private async Task<JsonElement> RunSmoke()
    {
        // Keep the real microphone closed during automated runs.
        Directory.CreateDirectory(_dir);
        var cfgPath = Path.Combine(_dir, "config.json");
        if (!File.Exists(cfgPath)) File.WriteAllText(cfgPath, """{"version":2,"devices":{"micEnabled":false}}""");
        var psi = new ProcessStartInfo(AppExe()) { ArgumentList = { "--data-dir", _dir, "--smoke-test" } };
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, p.ExitCode);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "smoke-result.json"))).RootElement;
    }

    [Fact]
    public async Task Starts_visits_pages_shuts_down_and_remembers_state()
    {
        var first = await RunSmoke();
        Assert.Equal(4, first.GetProperty("pagesVisited").GetArrayLength());
        Assert.True(first.GetProperty("miniOpened").GetBoolean());
        Assert.True(first.GetProperty("miniTopmost").GetBoolean()); // default: keep on top
        Assert.False(first.GetProperty("audioAtStart").GetBoolean());   // no sounds yet → audio engine not started
        Assert.True(first.GetProperty("hotkeysCreated").GetBoolean());  // global shortcuts are registered at startup

        // Import → shortcut → play → stop → remove, through the real engine.
        var sound = first.GetProperty("sound");
        Assert.True(sound.GetProperty("imported").GetBoolean());
        Assert.Equal("Ready", sound.GetProperty("state").GetString());
        Assert.InRange(sound.GetProperty("durationMs").GetDouble(), 950, 1050);
        Assert.Equal("Ctrl+Alt+Shift+F24", sound.GetProperty("hotkey").GetString());
        Assert.Equal(JsonValueKind.Null, sound.GetProperty("hotkeyError").ValueKind);
        if (sound.GetProperty("engineOk").GetBoolean()) // needs the virtual cable; true on the dev PC
        {
            Assert.True(sound.GetProperty("played").GetBoolean());
            Assert.True(sound.GetProperty("progressed").GetBoolean());
            Assert.True(sound.GetProperty("stopped").GetBoolean());
        }
        Assert.True(sound.GetProperty("removed").GetBoolean());

        // Sequence, random, per-sound route and profile round trip.
        var g = first.GetProperty("gaming");
        Assert.Equal("Ready", g.GetProperty("sequenceState").GetString());
        Assert.InRange(g.GetProperty("sequenceMs").GetDouble(), 2100, 2200); // 2 × 1 s + 150 ms gap
        Assert.True(g.GetProperty("randomPicked").GetBoolean());
        if (sound.GetProperty("engineOk").GetBoolean()) Assert.True(g.GetProperty("randomPlayed").GetBoolean());
        Assert.Equal("General use", g.GetProperty("profileBefore").GetString());
        Assert.Equal(JsonValueKind.Null, g.GetProperty("gamingHotkey").ValueKind); // Gaming never had that shortcut
        Assert.Equal("Both", g.GetProperty("gamingRoute").GetString());
        Assert.Equal("Ctrl+Alt+Shift+F23", g.GetProperty("backHotkey").GetString());
        Assert.Equal("MonitorOnly", g.GetProperty("backRoute").GetString());
        Assert.True(g.GetProperty("seqGone").GetBoolean());
        Assert.Equal(4, g.GetProperty("profileCount").GetInt32());

        var log = string.Join('\n', Directory.GetFiles(Path.Combine(_dir, "Logs"), "*.jsonl").Select(File.ReadAllText));
        Assert.Contains("Shutdown complete", log);
        Assert.DoesNotContain("\"lvl\":\"error\"", log);

        var second = await RunSmoke();
        Assert.Equal("Shortcuts", second.GetProperty("startPage").GetString()); // persisted across restart
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
