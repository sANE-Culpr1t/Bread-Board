using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using BreadBoard.App.Views.Dialogs;
using static BreadBoard.App.Views.Pages.PageKit;

namespace BreadBoard.App.Views.Pages;

/// <summary>
/// Every shortcut in one place: sound shortcuts and global actions, whether Windows accepted
/// each one, and a Change button that records a new combination (conflicts are checked).
/// </summary>
public sealed class ShortcutsPage : UserControl
{
    private readonly SoundboardController _sb;

    public ShortcutsPage(SoundboardController sb)
    {
        _sb = sb;
        sb.HotkeysChanged += Build;
        sb.LibraryChanged += Build;
        Build();
    }

    private void Build()
    {
        var root = new StackPanel();
        root.Children.Add(Header("Shortcuts", "Global shortcuts work while Bread Board is minimized and while a game has focus."));

        var states = _sb.Hotkeys.ToDictionary(h => h.OwnerKey);
        var problems = states.Values.Count(h => h.Error is not null);
        if (problems > 0)
        {
            var chip = Pending($"{problems} shortcut{(problems == 1 ? " isn't" : "s aren't")} active — see the red notes below");
            ((TextBlock)chip.Child).Foreground = Res("BadBrush");
            root.Children.Add(chip);
        }

        root.Children.Add(SectionTitle("Sounds"));
        var sounds = new StackPanel();
        var items = _sb.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var st = states.GetValueOrDefault($"sound:{it.Id}") ?? new HotkeyState($"sound:{it.Id}", it.Name, it.Shortcut.Length > 0 ? it.Shortcut : null, null);
            sounds.Children.Add(ShortcutRow(it.Glyph, it.TintBrush, it.Name, $"{it.Category} · {it.ModeText}", st, i == items.Count - 1));
        }
        if (items.Count == 0) sounds.Children.Add(Row("No sounds yet", "Add sounds on the Soundboard page, then give them shortcuts here or in Edit.", new TextBlock(), last: true));
        root.Children.Add(Card(sounds, new Thickness(20, 4, 20, 4)));

        root.Children.Add(SectionTitle("Global actions"));
        var global = new StackPanel();
        var ink = Res("InkBrush");
        global.Children.Add(ShortcutRow(Glyph.Stop, ink, "Stop all sounds", "Silences everything that is playing", S(states, "global:stopAll", "Stop all sounds", _sb.Config.Hotkeys.StopAll), false));
        global.Children.Add(ShortcutRow(Glyph.Game, ink, "Toggle mini-player", "Shows or hides Gaming Mode", S(states, "global:toggleMini", "Toggle mini-player", _sb.Config.Hotkeys.ToggleMini), false));
        global.Children.Add(ShortcutRow("\uE8B1", ink, "Play a random sound", "From your favourites, or from all sounds", S(states, "global:random", "Play a random sound", _sb.Config.Hotkeys.RandomSound), false));
        global.Children.Add(ShortcutRow(Glyph.MicOff, ink, "Mute microphone", "Toggles the mic channel", S(states, "global:muteMic", "Mute microphone", _sb.Config.Hotkeys.MuteMic, pending: true), false));
        global.Children.Add(ShortcutRow(Glyph.Mic, ink, "Push-to-talk", _sb.Config.Processing.PushToTalk.Enabled ? "Hold to send your microphone (the key still works in other apps)" : "Off: turn it on in Mixer › Microphone", S(states, "global:ptt", "Push-to-talk", _sb.Config.Processing.PushToTalk.Key, pending: true), true));
        root.Children.Add(Card(global, new Thickness(20, 4, 20, 4)));

        Content = Scroll(root);
    }

    /// <summary>Registration state, or the saved combo before registration has run.</summary>
    private static HotkeyState S(Dictionary<string, HotkeyState> states, string key, string label, string? combo, bool pending = false) =>
        states.GetValueOrDefault(key) ?? new HotkeyState(key, label, combo, null, pending);

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private Border ShortcutRow(string glyph, Brush tint, string name, string detail, HotkeyState state, bool last)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (state.Combo is { } combo)
        {
            foreach (var k in combo.Split('+'))
            {
                keys.Children.Add(new Border
                {
                    Background = Res("RaisedBrush"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Child = Text(k, 12, weight: FontWeights.SemiBold),
                });
            }
        }
        else keys.Children.Add(Text("Not set", 13, muted: true));

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(keys);
        if (state.Pending)
        {
            var chip = Pending("Iteration 4");
            chip.Margin = new Thickness(12, 0, 0, 0);
            chip.ToolTip = "Microphone controls arrive with the mixer in Iteration 4.";
            right.Children.Add(chip);
        }
        else
        {
            var change = PageKit.Button(state.Combo is null ? "Set" : "Change", "GhostButton");
            change.Margin = new Thickness(12, 0, 0, 0);
            change.Click += async (_, _) => await HotkeyDialog.EditAsync(_sb, state);
            right.Children.Add(change);
        }

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph, 18);
        icon.Foreground = tint;
        icon.Width = 28;
        title.Children.Add(icon);
        var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        texts.Children.Add(Text(name, 15));
        if (state.Error is { } err)
        {
            var e = Text($"Not active: {err}", 12);
            e.Foreground = Res("BadBrush");
            texts.Children.Add(e);
        }
        else texts.Children.Add(Text(detail, 12, muted: true));
        title.Children.Add(texts);

        var dock = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(title);
        return new Border
        {
            Child = dock,
            BorderBrush = Res("LineBrush"),
            BorderThickness = last ? new Thickness(0) : new Thickness(0, 0, 0, 1),
        };
    }
}
