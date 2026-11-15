using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BreadBoard.App.Theme;

/// <summary>Dark title-bar colours and Windows 11 rounded corners for chrome-less windows.</summary>
internal static class WindowFrame
{
    public static void Apply(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        var round = 2;
        _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE: round */, ref round, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
