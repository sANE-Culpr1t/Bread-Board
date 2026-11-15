using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;

namespace BreadBoard.Audio.Input;

/// <summary>
/// System-wide hotkeys via RegisterHotKey: they fire while any other app (Discord, a game) has focus.
/// Runs its own message-loop thread. For hold-to-play, key release is detected by briefly
/// watching the key state only while it is held (no idle polling).
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private sealed record Binding(int Id, Hotkey Hotkey, uint Vk, Action OnDown, Action? OnUp);

    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Dictionary<int, Binding> _bindings = []; // message thread only
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private int _nextId = 1;

    public GlobalHotkeyService()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Bread Board hotkeys" };
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Registers a hotkey. Returns null on success, otherwise why it failed (e.g. taken by another app).</summary>
    public Task<string?> RegisterAsync(Hotkey hotkey, Action onDown, Action? onUp = null)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(() =>
        {
            if (HotkeyRules.ReservedReason(hotkey) is { } why) { tcs.SetResult(why); return; }
            if (VirtualKey(hotkey.Key) is not { } vk) { tcs.SetResult($"Key '{hotkey.Key}' isn't supported"); return; }
            var id = _nextId++;
            if (!RegisterHotKey(0, id, Mods(hotkey.Modifiers) | MOD_NOREPEAT, vk))
            {
                var err = Marshal.GetLastWin32Error();
                tcs.SetResult(err == ERROR_HOTKEY_ALREADY_REGISTERED
                    ? $"{hotkey} is already used by another program"
                    : $"Windows refused {hotkey} (error {err})");
                return;
            }
            _bindings[id] = new Binding(id, hotkey, vk, onDown, onUp);
            tcs.SetResult(null);
        });
        return tcs.Task;
    }

    public void UnregisterAll() => Invoke(() =>
    {
        foreach (var id in _bindings.Keys) UnregisterHotKey(0, id);
        _bindings.Clear();
    });

    private void Invoke(Action a)
    {
        _work.Enqueue(a);
        PostThreadMessage(_threadId, WM_APP, 0, 0);
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0); // creates the thread's message queue
        _ready.Set();
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_APP)
            {
                while (_work.TryDequeue(out var a))
                {
                    try { a(); } catch (Exception ex) { Log.Error("Hotkey thread action failed", ex); }
                }
            }
            else if (msg.message == WM_HOTKEY && _bindings.TryGetValue((int)msg.wParam, out var b))
            {
                Fire(b.OnDown);
                if (b.OnUp is { } up) _ = WatchRelease(b.Vk, up);
            }
            else if (msg.message == WM_QUIT) break;
        }
        foreach (var id in _bindings.Keys) UnregisterHotKey(0, id);
    }

    private static async Task WatchRelease(uint vk, Action onUp)
    {
        while ((GetAsyncKeyState((int)vk) & 0x8000) != 0) await Task.Delay(15).ConfigureAwait(false);
        Fire(onUp);
    }

    private static void Fire(Action a)
    {
        try { a(); } catch (Exception ex) { Log.Error("Hotkey handler failed", ex); }
    }

    /// <summary>Maps the shared key names ("A", "1", "Num1", "F5", "Space"…) to Win32 virtual-key codes.</summary>
    public static uint? VirtualKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return char.ToUpperInvariant(key[0]);
        if (key.StartsWith("Num", StringComparison.Ordinal) && key.Length == 4 && char.IsAsciiDigit(key[3])) return (uint)(0x60 + key[3] - '0');
        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24) return (uint)(0x70 + f - 1);
        return key switch
        {
            "Space" => 0x20, "Enter" => 0x0D, "Tab" => 0x09, "Esc" => 0x1B, "Backspace" => 0x08,
            "Insert" => 0x2D, "Delete" => 0x2E, "Home" => 0x24, "End" => 0x23, "PageUp" => 0x21, "PageDown" => 0x22,
            "Up" => 0x26, "Down" => 0x28, "Left" => 0x25, "Right" => 0x27,
            "NumPlus" => 0x6B, "NumMinus" => 0x6D, "NumMul" => 0x6A, "NumDiv" => 0x6F, "NumDot" => 0x6E,
            "Pause" => 0x13, "ScrollLock" => 0x91,
            "`" => 0xC0, "-" => 0xBD, "=" => 0xBB, "[" => 0xDB, "]" => 0xDD, "\\" => 0xDC, ";" => 0xBA, "'" => 0xDE, "," => 0xBC, "." => 0xBE, "/" => 0xBF,
            _ => null,
        };
    }

    private static uint Mods(HotkeyModifiers m) =>
        (m.HasFlag(HotkeyModifiers.Alt) ? 1u : 0) | (m.HasFlag(HotkeyModifiers.Ctrl) ? 2u : 0) |
        (m.HasFlag(HotkeyModifiers.Shift) ? 4u : 0) | (m.HasFlag(HotkeyModifiers.Win) ? 8u : 0);

    public void Dispose()
    {
        PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(1000);
        _ready.Dispose();
    }

    private const uint WM_HOTKEY = 0x0312, WM_APP = 0x8000, WM_QUIT = 0x0012, MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int ptX, ptY; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, nint hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
