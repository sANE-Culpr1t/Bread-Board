using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using static BreadBoard.App.Theme.PageKit;

namespace BreadBoard.App.Settings;

/// <summary>Gaming Mode, playback, profiles, data folders and version.</summary>
public sealed class SettingsPage : UserControl
{
    public SettingsPage(ConfigService config, SoundboardController sb, Action showMini)
    {
        var ui = config.Current.Ui;
        var root = new StackPanel();
        root.Children.Add(Header("Settings", "Preferences are saved automatically."));

        root.Children.Add(SectionTitle("Gaming Mode"));
        var openOnStart = Switch(ui.MiniOpenOnStart, true, "Open the mini-player whenever Bread Board starts");
        OnToggle(openOnStart, () => config.Current.Ui.MiniOpenOnStart, on => config.Update(c => c.Ui.MiniOpenOnStart = on));
        var onTop = Switch(ui.MiniTopmost, true, "Keep the mini-player above other windows, including games in windowed mode");
        OnToggle(onTop, () => config.Current.Ui.MiniTopmost, v => config.Update(c => c.Ui.MiniTopmost = v));
        config.Changed += c => Dispatcher.BeginInvoke(() => onTop.IsChecked = c.Ui.MiniTopmost); // the mini-player's pin changes it too
        var open = PageKit.Button("Open mini-player", glyph: Glyph.Game);
        open.Click += (_, _) => showMini();
        var gaming = new StackPanel();
        gaming.Children.Add(Row("Open mini-player on start", "Shows the floating window as soon as Bread Board launches.", openOnStart));
        gaming.Children.Add(Row("Keep mini-player on top", "Stays above other windows, including windowed games.", onTop));
        var opacity = new Slider { Width = 160, Minimum = 40, Maximum = 100, Value = ui.MiniOpacity, IsSnapToTickEnabled = true, TickFrequency = 5 };
        System.Windows.Automation.AutomationProperties.SetName(opacity, "Mini-player opacity");
        var opacityText = Text($"{ui.MiniOpacity}%", 14);
        opacityText.Width = 44;
        opacityText.TextAlignment = TextAlignment.Right;
        opacity.ValueChanged += (_, _) =>
        {
            var v = (int)opacity.Value;
            opacityText.Text = $"{v}%";
            config.Update(c => c.Ui.MiniOpacity = v);
        };
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal };
        opacityRow.Children.Add(opacity);
        opacityRow.Children.Add(opacityText);
        gaming.Children.Add(Row("Mini-player opacity", "See through it to the game behind (40-100 %).", opacityRow));
        gaming.Children.Add(Row("Mini-player", "A compact window for use while gaming.", open));
        var tray = Switch(ui.CloseToTray, true, "Closing the window keeps Bread Board in the tray; quit from the tray icon");
        OnToggle(tray, () => config.Current.Ui.CloseToTray, on => config.Update(c => c.Ui.CloseToTray = on));
        gaming.Children.Add(Row("Keep running in the tray when closed", "Shortcuts and the Bread Board mic keep working. Quit from the tray icon to close Bread Board and hide its mic.", tray, last: true));
        root.Children.Add(Card(gaming));

        root.Children.Add(SectionTitle("Playback"));
        var pb = config.Current.Playback;
        var playback = new StackPanel();
        var hear = Switch(config.Current.Devices.MonitorEnabled, true, "Play the monitor mix (your sounds) on your headphones as well as the virtual mic");
        OnToggle(hear, () => config.Current.Devices.MonitorEnabled, async on => await sb.SetDevicesAsync(d => d.MonitorEnabled = on));
        sb.MixChanged += () => hear.IsChecked = config.Current.Devices.MonitorEnabled;
        sb.EngineChanged += () => hear.IsChecked = config.Current.Devices.MonitorEnabled;
        playback.Children.Add(Row("Hear sounds on this PC", "Plays the monitor mix on your headphones (pick the device and what goes in it on the Mixer page).", hear));
        var stacking = Switch(pb.AllowStacking, true, "Pressing a one-shot sound again plays another copy over it");
        OnToggle(stacking, () => config.Current.Playback.AllowStacking, async on =>
        {
            config.Update(c => c.Playback.AllowStacking = on);
            await sb.ApplyPlaybackSettingsAsync();
        });
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
        playback.Children.Add(Row("Output", "In Discord or your game, pick CABLE Output as the microphone. Devices are chosen on the Mixer page.", output, last: true));
        root.Children.Add(Card(playback));

        root.Children.Add(SectionTitle("Profiles"));
        var intro = Text("Each profile keeps its own sound shortcuts and routing, global shortcuts, mixer levels, voice processing, microphone/monitor switches and app choice. Your sounds are shared. Changes always go to the active profile; switching keeps them.", 13, muted: true);
        intro.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(intro);
        var profiles = new StackPanel();
        void BuildProfiles()
        {
            profiles.Children.Clear();
            for (var i = 0; i < ProfileService.All.Length; i++)
            {
                var (name, description) = ProfileService.All[i];
                var active = name == sb.ActiveProfile;
                FrameworkElement control;
                if (active)
                {
                    control = Chip("Active", "OkBrush");
                }
                else
                {
                    var b = PageKit.Button("Switch", "GhostButton", tooltip: $"Switch to {name}");
                    System.Windows.Automation.AutomationProperties.SetName(b, $"Switch to {name}");
                    b.Click += async (_, _) => await sb.SwitchProfileAsync(name);
                    control = b;
                }
                profiles.Children.Add(Row(name, description, control, last: i == ProfileService.All.Length - 1));
            }
        }
        BuildProfiles();
        sb.ProfileChanged += BuildProfiles;
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
