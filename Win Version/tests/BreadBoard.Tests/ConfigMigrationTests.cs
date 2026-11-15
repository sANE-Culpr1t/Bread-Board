using BreadBoard.Core.Config;
using BreadBoard.Core.Models;

namespace BreadBoard.Tests;

public class ConfigMigrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void V1_hear_on_this_pc_becomes_monitor(bool hear, bool expectedMonitor)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bb-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        var h = hear ? "true" : "false";
        var m = expectedMonitor ? "false" : "true"; // start opposite so the migration must change it
        File.WriteAllText(path, "{\"version\":1,\"playback\":{\"hearOnThisPc\":" + h + "},\"devices\":{\"monitorEnabled\":" + m + ",\"latencyMs\":33}}");
        var cfg = new ConfigStore(path).Load();
        Assert.Equal(expectedMonitor, cfg.Devices.MonitorEnabled);
        Assert.Equal(20, cfg.Devices.LatencyMs); // unsupported value reset
        Assert.Equal(AppConfig.CurrentVersion, cfg.Version);
        Directory.Delete(dir, true);
    }
}
