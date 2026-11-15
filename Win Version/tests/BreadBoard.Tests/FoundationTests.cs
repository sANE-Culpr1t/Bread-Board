using System.Diagnostics;
using System.Text.Json;
using BreadBoard.Core.Config;
using BreadBoard.Core.Logging;
using BreadBoard.Platform;

namespace BreadBoard.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bb-cfgsvc-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Debounces_saves_and_flushes_on_dispose()
    {
        var path = Path.Combine(_dir, "config.json");
        var svc = new ConfigService(new ConfigStore(path), debounceMs: 150);
        svc.Update(c => c.Ui.LastPage = "Mixer");
        svc.Update(c => c.Ui.LastPage = "Settings");
        Assert.False(File.Exists(path)); // not written synchronously
        await Task.Delay(600);
        Assert.Contains("Settings", File.ReadAllText(path));

        svc.Update(c => c.Ui.LastPage = "Shortcuts");
        svc.Dispose(); // shutdown path writes immediately
        Assert.Equal("Shortcuts", new ConfigStore(path).Load().Ui.LastPage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

public class SingleInstanceTests
{
    [Fact]
    public async Task Second_acquire_fails_and_signals_the_first()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bb-single-" + Guid.NewGuid().ToString("N"));
        var activated = new TaskCompletionSource();
        using var first = SingleInstance.TryAcquire(dir);
        Assert.NotNull(first);
        first!.ListenForActivation(() => activated.TrySetResult());

        // A second launch runs on another thread (mutex ownership is per thread).
        var second = await Task.Run(() => SingleInstance.TryAcquire(dir));
        Assert.Null(second);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

public class LogTests
{
    [Fact]
    public void Writes_json_lines_with_category()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bb-log-" + Guid.NewGuid().ToString("N"));
        Log.Init(dir);
        Log.Info("hello from test");
        Log.Error("boom", new InvalidOperationException("bad"));
        Log.Flush();
        var lines = Directory.GetFiles(dir, "*.jsonl").SelectMany(File.ReadAllLines).ToList();
        var hello = lines.Select(l => JsonDocument.Parse(l).RootElement).First(e => e.GetProperty("msg").GetString() == "hello from test");
        Assert.Equal("FoundationTests", hello.GetProperty("cat").GetString());
        Assert.Contains(lines, l => l.Contains("InvalidOperationException", StringComparison.Ordinal));
    }
}

/// <summary>Launches the real app (built Debug exe) with an isolated data folder.</summary>
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
        Assert.False(first.GetProperty("webView2Loaded").GetBoolean());

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
