using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreadBoard.App.Theme;

/// <summary>Icons of other apps' executables (for the app picker), cached per path.</summary>
public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (Cache.TryGetValue(exePath, out var cached)) return cached;
        ImageSource? image = null;
        var large = new nint[1];
        try
        {
            if (ExtractIconEx(exePath, 0, large, null, 1) > 0 && large[0] != 0)
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                image = bmp;
            }
        }
        catch (COMException) { }
        finally
        {
            if (large[0] != 0) DestroyIcon(large[0]);
        }
        return Cache[exePath] = image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, nint[]? large, nint[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}
