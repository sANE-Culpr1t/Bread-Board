using System.Runtime.InteropServices;

namespace BreadBoard.Tests;

/// <summary>Synthetic key presses (SendInput): they go to whatever window has focus.</summary>
internal static class Keys
{
    public static void Send(ushort vk, bool up)
    {
        var input = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } };
        Assert.Equal(1u, SendInput(1, [input], Marshal.SizeOf<INPUT>()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
