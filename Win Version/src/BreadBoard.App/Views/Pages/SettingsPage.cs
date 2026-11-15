using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using static BreadBoard.App.Views.Pages.PageKit;

namespace BreadBoard.App.Views.Pages;

/// <summary>
/// Settings layout. Window/mini-player preferences and the data-folder buttons work now;
/// audio, playback and profile settings are disabled until their iterations.
/// </summary>
public sealed class SettingsPage : UserControl
{
    public SettingsPage(ConfigService config, MainWindow window)
    {
        var ui = config.Current.Ui;
        var root = new StackPanel();
        root.Children.Add(Header("Settings", "Preferences are saved automatically."));

        root.Children.Add(SectionTitle("Gaming Mode"));
        var openOnStart = Switch(ui.MiniOpenOnStart, true, "Open the mini-player whenever Bread Board starts");
        openOnStart.Click += (_, _) => config.Update(c => c.Ui.MiniOpenOnStart = openOnStart.IsChecked == true);
        var onTop = Switch(ui.MiniTopmost, true, "Keep the mini-player above other windows, including games in windowed mode");
        onTop.Click += (_, _) =>
        {
            var v = onTop.IsChecked == true;
            config.Update(c => c.Ui.MiniTopmost = v);
            window.MiniPlayer?.SetTopmost(v);
        };
        var open = PageKit.Button("Open mini-player", glyph: Theme.Glyph.Game);
        open.Click += (_, _) => window.ShowMini();
        var gaming = new StackPanel();
        gaming.Children.Add(Row("Open mini-player on start", "Shows the floating window as soon as Bread Board launches.", openOnStart));
        gaming.Children.Add(Row("Keep mini-player on top", "Stays above other windows, including windowed games.", onTop));
        gaming.Children.Add(Row("Mini-player", "A compact window for use while gaming.", open, last: true));
        root.Children.Add(Card(gaming));

        root.Children.Add(SectionTitle("Playback"));
        var playback = new StackPanel();
        playback.Children.Add(Row("Allow sound stacking", "Pressing a sound again plays another copy over it.",
            Switch(config.Current.Playback.AllowStacking, false, "Arrives in Iteration 2.")));
        playback.Children.Add(Row("Maximum simultaneous sounds", "The oldest sound stops when the limit is reached.",
            new Slider { Width = 180, Minimum = 1, Maximum = 16, Value = config.Current.Playback.MaxVoices, IsEnabled = false, ToolTip = "Arrives in Iteration 2." }));
        playback.Children.Add(Row("Missing files", "What to do when a sound's file was moved or deleted.",
            Pending("Iteration 2"), last: true));
        root.Children.Add(Card(playback));

        root.Children.Add(SectionTitle("Profiles"));
        var profiles = new StackPanel();
        profiles.Children.Add(Row("Gaming · General use · Streaming · Custom",
            "Save and switch sound assignments, hotkeys, mixer levels and routing.", Pending("Iteration 5"), last: true));
        root.Children.Add(Card(profiles));

        root.Children.Add(SectionTitle("Data"));
        var data = new StackPanel();
        data.Children.Add(Row("Settings folder", AppPaths.Roaming, OpenFolder(AppPaths.Roaming)));
        data.Children.Add(Row("Sound library", AppPaths.SoundLibrary, OpenFolder(AppPaths.SoundLibrary)));
        data.Children.Add(Row("Logs", AppPaths.Logs, OpenFolder(AppPaths.Logs), last: true));
        root.Children.Add(Card(data));

        root.Children.Add(SectionTitle("About"));
        var about = new StackPanel();
        about.Children.Add(Row($"Bread Board {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)}",
            $".NET {Environment.Version} · Windows {Environment.OSVersion.Version}", new TextBlock(), last: true));
        root.Children.Add(Card(about));

        Content = Scroll(root);
    }

    private static Button OpenFolder(string path)
    {
        var b = PageKit.Button("Open", "GhostButton", tooltip: path);
        b.Click += (_, _) =>
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        };
        return b;
    }
}
