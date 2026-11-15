using BreadBoard.Core.Hotkeys;
using BreadBoard.Platform.Hotkeys;

namespace BreadBoard.Tests;

public class GlobalHotkeyTests
{
    [Theory]
    [InlineData("A")] [InlineData("7")] [InlineData("Num1")] [InlineData("F13")] [InlineData("Space")] [InlineData("PageDown")] [InlineData("/")]
    public void Key_names_round_trip(string name) =>
        Assert.Equal(name, GlobalHotkeyService.KeyName(GlobalHotkeyService.VirtualKey(name)!.Value));

    [Fact, Trait("Category", "Hardware")] // registers system-wide shortcuts
    public async Task Rejects_reserved_and_reports_conflicts_between_programs()
    {
        using var a = new GlobalHotkeyService();
        using var b = new GlobalHotkeyService(); // stands in for "another program" (separate registrations)
        Assert.NotNull(await a.RegisterAsync(new Hotkey(HotkeyModifiers.Alt, "F4"), () => { }));

        var combo = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "F23");
        Assert.Null(await a.RegisterAsync(combo, () => { }));
        var err = await b.RegisterAsync(combo, () => { });
        Assert.NotNull(err);
        Assert.Contains("already used", err, StringComparison.Ordinal);

        a.UnregisterAll();
        await Task.Delay(50);
        Assert.Null(await b.RegisterAsync(combo, () => { })); // freed after unregister
    }

    [Fact, Trait("Category", "Hardware")] // presses Ctrl+Alt+Shift+F22 in whatever window has focus
    public async Task Fires_while_another_window_has_focus()
    {
        // RegisterHotKey is system-wide: a synthetic key press reaches us no matter which window is active.
        using var svc = new GlobalHotkeyService();
        var fired = new TaskCompletionSource();
        var released = new TaskCompletionSource();
        var combo = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "F22");
        Assert.Null(await svc.RegisterAsync(combo, () => fired.TrySetResult(), () => released.TrySetResult()));

        const ushort ctrl = 0x11, alt = 0x12, shift = 0x10, f22 = 0x85;
        Keys.Send(ctrl, false); Keys.Send(alt, false); Keys.Send(shift, false); Keys.Send(f22, false);
        await Task.Delay(60);
        Keys.Send(f22, true); Keys.Send(shift, true); Keys.Send(alt, true); Keys.Send(ctrl, true);

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await released.Task.WaitAsync(TimeSpan.FromSeconds(3)); // hold-mode release detection
    }

}
