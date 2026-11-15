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
        if (host.Soundboard.Items.Count == 0) await SeedAsync(host).ConfigureAwait(true);

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

        foreach (var (name, id) in new[] { ("6-AddSound", (string?)null), ("7-EditSound", host.Soundboard.Items.FirstOrDefault()?.Id) })
        {
            var dlg = Views.Dialogs.SoundEditorDialog.Preview(host.Soundboard, id);
            dlg.Show();
            await Settle().ConfigureAwait(true);
            Save(dlg, Path.Combine(dir, $"{name}.png"));
            dlg.Close();
        }
        host.Soundboard.StopAll();

        Log.Info($"Screenshots written to {dir}");
        window.Close();
    }

    /// <summary>Fills an empty (screenshot-only) library with generated tones so pads show real states.</summary>
    private static async Task SeedAsync(AppHost host)
    {
        var sb = host.Soundboard;
        var src = Path.Combine(Core.AppPaths.Roaming, "seed");
        Directory.CreateDirectory(src);
        (string Name, string Cat, string Icon, string Tint, string? Key, Core.Models.PlayMode Mode, bool Fav, double Secs)[] seed =
        [
            ("Bruh", "Memes", "emoji", "beige", "Num1", Core.Models.PlayMode.OneShot, true, 0.9),
            ("Vine Boom", "Memes", "volume", "cream", "Num2", Core.Models.PlayMode.OneShot, true, 1.4),
            ("Airhorn", "Effects", "bolt", "amber", "Num3", Core.Models.PlayMode.OneShot, false, 2.2),
            ("Laugh Track", "Reactions", "emoji", "amber", "Num4", Core.Models.PlayMode.OneShot, true, 2.6),
            ("Drumroll", "Music", "music", "beige", "Num7", Core.Models.PlayMode.Hold, false, 4.0),
            ("Siren", "Effects", "alert", "clay", "Num8", Core.Models.PlayMode.Loop, false, 2.0),
            ("Applause", "Reactions", "heart", "sage", "Num9", Core.Models.PlayMode.Toggle, false, 5.2),
            ("Sad Trombone", "Reactions", "music", "mauve", "Ctrl+Alt+Shift+F17", Core.Models.PlayMode.OneShot, false, 3.0),
        ];
        foreach (var s in seed)
        {
            var wav = WriteSilence(Path.Combine(src, s.Name.Replace(' ', '_') + ".wav"), s.Secs);
            if (await sb.ImportFilesAsync([wav], s.Cat).ConfigureAwait(true) is [var item, ..])
                await sb.SaveAsync(item.Id, new Services.SoundEdit(s.Name, s.Cat, s.Key, s.Mode, 80, s.Fav, s.Icon, s.Tint)).ConfigureAwait(true);
        }
        // One sound whose file has gone missing.
        var missing = sb.Library.Add(Path.Combine(src, "deleted.wav"), "Old Intro", "Music", null);
        await sb.AddAndLoadAsync(missing).ConfigureAwait(true);
        if (sb.Items.FirstOrDefault(i => i.Name == "Siren") is { } siren) sb.Press(siren.Id); // show the playing state
        await Task.Delay(600).ConfigureAwait(true);
    }

    /// <summary>Silent WAV (screenshots shouldn't make noise).</summary>
    private static string WriteSilence(string path, double seconds)
    {
        const int rate = 48000;
        var n = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        w.Write(new byte[n * 2]);
        return path;
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
