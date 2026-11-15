using BreadBoard.Core.Config;

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
