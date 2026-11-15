using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bb-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Round_trips_config()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var cfg = new AppConfig();
        cfg.Sounds.Add(new SoundEntry { Name = "Bruh", Mode = PlayMode.Hold, Hotkey = "1" });
        cfg.Devices.AppSource = new AppSourceSelection { ProcessName = "brave" };
        store.Save(cfg);

        var loaded = store.Load();
        Assert.Equal("Bruh", loaded.Sounds[0].Name);
        Assert.Equal(PlayMode.Hold, loaded.Sounds[0].Mode);
        Assert.Equal("brave", loaded.Devices.AppSource!.ProcessName);
        Assert.Contains("\"hold\"", File.ReadAllText(store.Path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_is_backed_up()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ not json");
        var cfg = new ConfigStore(path).Load();
        Assert.Empty(cfg.Sounds);
        Assert.Single(Directory.GetFiles(_dir, "config.json.bad-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
