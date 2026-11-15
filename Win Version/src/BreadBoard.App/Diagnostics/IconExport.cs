using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreadBoard.App.Diagnostics;

/// <summary>
/// --export-icon PATH [--export-png DIR]: renders the vector logo (Theme/Logo.xaml) into a multi-size
/// Windows .ico (PNG-compressed frames) and optional PNGs. No window is shown. Used to (re)build Assets/BreadBoard.ico.
/// </summary>
public static class IconExport
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    public static void Run(string icoPath, string? pngDir)
    {
        var logo = (ImageSource)Application.Current.FindResource("LogoImage");
        var frames = Sizes.Select(size => (size, png: Render(logo, size))).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(icoPath))!);
        using (var w = new BinaryWriter(File.Create(icoPath)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count); // ICONDIR
            var offset = 6 + 16 * frames.Count;
            foreach (var (size, png) in frames)                                  // ICONDIRENTRY
            {
                w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(png.Length); w.Write(offset);
                offset += png.Length;
            }
            foreach (var (_, png) in frames) w.Write(png);
        }

        if (pngDir is not null)
        {
            Directory.CreateDirectory(pngDir);
            foreach (var (size, png) in frames) File.WriteAllBytes(Path.Combine(pngDir, $"logo-{size}.png"), png);
        }
    }

    private static byte[] Render(ImageSource logo, int size)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen()) dc.DrawImage(logo, new Rect(0, 0, size, size));
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
