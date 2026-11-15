using BreadBoard.Platform.Hotkeys;

namespace BreadBoard.Tests;

/// <summary>Presses F19 system-wide.</summary>
[Trait("Category", "Hardware")]
public class PushToTalkKeyTests
{
    [Fact]
    public async Task Watches_a_key_without_taking_it_from_other_apps()
    {
        using var first = new GlobalHotkeyService();
        using var second = new GlobalHotkeyService(); // stands in for any other app listening to the key
        var events1 = new List<bool>();
        var events2 = new List<bool>();
        var done = new TaskCompletionSource();
        first.WatchKey("F19", d => { lock (events1) events1.Add(d); });
        second.WatchKey("F19", d => { lock (events2) { events2.Add(d); if (events2.Count == 2) done.TrySetResult(); } });
        await Task.Delay(200);

        Keys.Send(0x82, false); Keys.Send(0x82, false); // auto-repeat: reported once
        await Task.Delay(50);
        Keys.Send(0x82, true);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        lock (events1) Assert.Equal([true, false], events1);
        lock (events2) Assert.Equal([true, false], events2); // the key passed through the first hook
        first.WatchKey(null, null);
    }

}
