using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.Navigation;
using BreadBoard.App.Views;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Diagnostics;

/// <summary>
/// --screenshots DIR: renders every page (wide and compact widths) and the mini-player to PNG,
/// then exits. Used to review the UI without clicking through it.
/// </summary>
public static class ScreenshotTour
{
    public static async Task RunAsync(AppHost host, MainWindow window, string dir)
    {
        Directory.CreateDirectory(dir);
        var rendered = new TaskCompletionSource();
        window.ContentRendered += (_, _) => rendered.TrySetResult();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);

        window.WindowState = WindowState.Normal;
        foreach (var (width, height, suffix) in new[] { (1440.0, 900.0, "wide"), (960.0, 720.0, "compact") })
        {
            window.Width = width;
            window.Height = height;
            foreach (var page in Enum.GetValues<PageId>())
            {
                host.Navigation.Navigate(page);
                await Settle().ConfigureAwait(true);
                Save(window, Path.Combine(dir, $"{(int)page + 1}-{page}-{suffix}.png"));
            }
        }

        window.Width = 1440;
        host.Navigation.Navigate(PageId.Soundboard);
        window.ShowMini();
        await Settle().ConfigureAwait(true);
        if (window.MiniPlayer is { } mini) Save(mini, Path.Combine(dir, "5-MiniPlayer.png"));

        Log.Info($"Screenshots written to {dir}");
        window.Close();
    }

    private static async Task Settle()
    {
        await Task.Delay(350).ConfigureAwait(true); // page fade
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Save(Window w, string path)
    {
        var root = (FrameworkElement)w.Content;
        var dpi = VisualTreeHelper.GetDpi(root);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Paint the window background first so the PNG isn't transparent.
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(w.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
