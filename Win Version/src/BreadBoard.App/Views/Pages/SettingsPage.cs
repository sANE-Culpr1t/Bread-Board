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
    public SettingsPage(ConfigService config, MainWindow window, Services.SoundboardController sb)
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
        var pb = config.Current.Playback;
        var playback = new StackPanel();
        var hear = Switch(pb.HearOnThisPc, true, "Play sounds on your default speakers or headphones as well as the virtual mic");
        hear.Click += async (_, _) =>
        {
            config.Update(c => c.Playback.HearOnThisPc = hear.IsChecked == true);
            await sb.ApplyPlaybackSettingsAsync(devices: true);
        };
        playback.Children.Add(Row("Hear sounds on this PC", "Also plays them on your default speakers or headphones, so you hear what others hear.", hear));
        var stacking = Switch(pb.AllowStacking, true, "Pressing a one-shot sound again plays another copy over it");
        stacking.Click += async (_, _) =>
        {
            config.Update(c => c.Playback.AllowStacking = stacking.IsChecked == true);
            await sb.ApplyPlaybackSettingsAsync();
        };
        playback.Children.Add(Row("Allow sound stacking", "Pressing a one-shot sound again plays another copy over it; otherwise it restarts.", stacking));
        var voices = new Slider { Width = 160, Minimum = 1, Maximum = 16, Value = pb.MaxVoices, IsSnapToTickEnabled = true, TickFrequency = 1 };
        var voicesText = Text($"{pb.MaxVoices}", 14);
        voicesText.Width = 28;
        voicesText.TextAlignment = TextAlignment.Right;
        voices.ValueChanged += (_, _) =>
        {
            var v = (int)voices.Value;
            voicesText.Text = v.ToString(System.Globalization.CultureInfo.CurrentCulture);
            config.Update(c => c.Playback.MaxVoices = v);
            _ = sb.ApplyPlaybackSettingsAsync();
        };
        var voicesRow = new StackPanel { Orientation = Orientation.Horizontal };
        voicesRow.Children.Add(voices);
        voicesRow.Children.Add(voicesText);
        playback.Children.Add(Row("Maximum simultaneous sounds", "The oldest sound stops when the limit is reached.", voicesRow));
        var output = Text(sb.EngineSummary, 13, muted: true);
        output.MaxWidth = 320;
        output.TextAlignment = TextAlignment.Right;
        sb.EngineChanged += () => output.Text = sb.EngineSummary;
        playback.Children.Add(Row("Output", "In Discord or your game, pick CABLE Output as the microphone. Choosing devices arrives in Iteration 4.", output, last: true));
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
