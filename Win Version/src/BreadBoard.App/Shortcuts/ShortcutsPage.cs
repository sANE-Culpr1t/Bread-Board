using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreadBoard.Core.Hotkeys;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using static BreadBoard.App.Theme.PageKit;

namespace BreadBoard.App.Shortcuts;

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

        var problems = _sb.Hotkeys.Count(h => h.Error is not null);
        if (problems > 0)
            root.Children.Add(Chip($"{problems} shortcut{(problems == 1 ? " isn't" : "s aren't")} active — see the red notes below", "BadBrush"));

        root.Children.Add(SectionTitle("Sounds"));
        var sounds = new StackPanel();
        var items = _sb.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            sounds.Children.Add(ShortcutRow(it.Glyph, it.TintBrush, it.Name, $"{it.Category} · {it.ModeText}", _sb.HotkeyFor(HotkeyRules.SoundOwner(it.Id)), i == items.Count - 1));
        }
        if (items.Count == 0) sounds.Children.Add(Row("No sounds yet", "Add sounds on the Soundboard page, then give them shortcuts here or in Edit.", new TextBlock(), last: true));
        root.Children.Add(Card(sounds, new Thickness(20, 4, 20, 4)));

        root.Children.Add(SectionTitle("Global actions"));
        var global = new StackPanel();
        var ink = Res("InkBrush");
        foreach (var g in HotkeyRules.Globals)
        {
            var (glyph, detail) = Look(g.Key);
            global.Children.Add(ShortcutRow(glyph, ink, g.Label, detail, _sb.HotkeyFor(g.Key), g == HotkeyRules.Globals[^1]));
        }
        root.Children.Add(Card(global, new Thickness(20, 4, 20, 4)));

        Content = Scroll(root);
    }

    private (string Glyph, string Detail) Look(string globalKey) => globalKey switch
    {
        HotkeyRules.StopAll => (Glyph.Stop, "Silences everything that is playing"),
        HotkeyRules.ToggleMini => (Glyph.Game, "Shows or hides Gaming Mode"),
        HotkeyRules.RandomSound => ("\uE8B1", "From your favourites, or from all sounds"),
        HotkeyRules.MuteMic => (Glyph.MicOff, "Toggles the mic channel"),
        HotkeyRules.PushToTalk => (Glyph.Mic, _sb.Config.Processing.PushToTalk.Enabled ? "Hold to send your microphone (the key still works in other apps)" : "Off: turn it on in Mixer › Microphone"),
        _ => throw new ArgumentOutOfRangeException(nameof(globalKey), globalKey),
    };

    private static Brush Res(string key) => Res<Brush>(key);

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
        var change = PageKit.Button(state.Combo is null ? "Set" : "Change", "GhostButton");
        change.Margin = new Thickness(12, 0, 0, 0);
        change.Click += async (_, _) => await HotkeyDialog.EditAsync(_sb, state);
        right.Children.Add(change);

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
