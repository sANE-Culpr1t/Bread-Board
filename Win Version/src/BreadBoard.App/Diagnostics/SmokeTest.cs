using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.Navigation;
using BreadBoard.Core;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Diagnostics;

/// <summary>
/// --smoke-test: waits for the first frame, visits every page, then closes the window through the
/// normal close path. Writes smoke-result.json next to the config so tests can assert on it.
/// </summary>
public static class SmokeTest
{
    public static async Task RunAsync(AppHost host, Window window)
    {
        var rendered = new TaskCompletionSource();
        window.ContentRendered += (_, _) => rendered.TrySetResult();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        var startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        var startPage = host.Navigation.Current;

        var visited = new List<string>();
        foreach (var page in Enum.GetValues<PageId>())
        {
            host.Navigation.Navigate(page);
            await Task.Delay(50).ConfigureAwait(true);
            visited.Add(page.ToString());
        }
        host.Navigation.Navigate(PageId.Shortcuts); // so the next launch can verify it was remembered

        // Mini-player: opens, honours the always-on-top setting, closes cleanly.
        var main = (Views.MainWindow)window;
        main.ShowMini();
        await Task.Delay(100).ConfigureAwait(true);
        var miniOpened = main.MiniPlayer is not null;
        var miniTopmost = main.MiniPlayer?.Topmost ?? false;
        main.MiniPlayer?.Close();

        var result = new
        {
            startupMs = Math.Round(startupMs),
            startPage = startPage.ToString(),
            pagesVisited = visited,
            miniOpened,
            miniTopmost,
            audioCreated = host.AudioCreated,
            hotkeysCreated = host.HotkeysCreated,
            webView2Loaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true),
        };
        await File.WriteAllTextAsync(Path.Combine(AppPaths.Roaming, "smoke-result.json"), JsonSerializer.Serialize(result)).ConfigureAwait(true);
        Log.Info("Smoke test finished; closing");
        window.Close();
    }
}
