using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;

namespace BreadBoard.Platform.Hotkeys;

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

    /// <summary>
    /// Watches one key without taking it away from other apps (push-to-talk: the key still types and still
    /// reaches the game). Uses a low-level keyboard hook on this thread, installed only while a key is watched.
    /// Pass null to stop. <paramref name="onChange"/> gets true on press and false on release.
    /// </summary>
    public void WatchKey(string? keyName, Action<bool>? onChange) => Invoke(() =>
    {
        if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
        _watch = null;
        _watchDown = false;
        if (keyName is null || onChange is null || VirtualKey(keyName) is not { } vk) return;
        _watchVk = vk;
        _watch = onChange;
        _hookProc ??= HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        if (_hook == 0) Log.Warn($"Keyboard hook for {keyName} failed (error {Marshal.GetLastWin32Error()})");
    });

    private nint _hook;
    private HookProc? _hookProc;
    private uint _watchVk;
    private Action<bool>? _watch;
    private bool _watchDown;

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _watch is { } watch && (uint)Marshal.ReadInt32(lParam) == _watchVk)
        {
            var msg = (uint)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN && !_watchDown) { _watchDown = true; Fire(() => watch(true)); }
            else if (msg is WM_KEYUP or WM_SYSKEYUP && _watchDown) { _watchDown = false; Fire(() => watch(false)); }
        }
        return CallNextHookEx(_hook, code, wParam, lParam); // never swallow the key
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
        if (_hook != 0) UnhookWindowsHookEx(_hook);
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

    /// <summary>Reverse of <see cref="VirtualKey"/>: the shared key name for a virtual-key code, or null if unsupported.</summary>
    public static string? KeyName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return $"Num{vk - 0x60}";
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x70 + 1}";
        foreach (var name in NamedKeys)
            if (VirtualKey(name) == vk) return name;
        return null;
    }

    private static readonly string[] NamedKeys =
    [
        "Space", "Enter", "Tab", "Esc", "Backspace", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
        "Up", "Down", "Left", "Right", "NumPlus", "NumMinus", "NumMul", "NumDiv", "NumDot", "Pause", "ScrollLock",
        "`", "-", "=", "[", "]", "\\", ";", "'", ",", ".", "/",
    ];

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
    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

    private delegate nint HookProc(int code, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

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

