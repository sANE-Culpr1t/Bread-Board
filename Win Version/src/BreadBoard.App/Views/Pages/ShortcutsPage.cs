using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreadBoard.App.Theme;
using BreadBoard.App.ViewModels;
using static BreadBoard.App.Views.Pages.PageKit;

namespace BreadBoard.App.Views.Pages;

/// <summary>Shortcut list layout (sample pads + global actions). Assigning keys arrives in Iteration 2.</summary>
public sealed class ShortcutsPage : UserControl
{
    public ShortcutsPage()
    {
        var root = new StackPanel();
        root.Children.Add(Header("Shortcuts", "Global shortcuts work while Bread Board is minimized and while a game has focus."));
        root.Children.Add(Pending("Assigning and testing shortcuts arrives in Iteration 2 · rows below use the sample pads"));

        root.Children.Add(SectionTitle("Sounds"));
        var sounds = new StackPanel();
        var pads = SamplePad.All;
        for (var i = 0; i < pads.Count; i++)
            sounds.Children.Add(ShortcutRow(pads[i].Glyph, pads[i].TintBrush, pads[i].Name, $"{pads[i].Category} · {pads[i].Mode}", pads[i].Shortcut, i == pads.Count - 1));
        root.Children.Add(Card(sounds, new Thickness(20, 4, 20, 4)));

        root.Children.Add(SectionTitle("Global actions"));
        var global = new StackPanel();
        var ink = (Brush)Application.Current.FindResource("InkBrush");
        global.Children.Add(ShortcutRow(Glyph.Stop, ink, "Stop all sounds", "Silences everything that is playing", "Ctrl+Shift+S", false));
        global.Children.Add(ShortcutRow(Glyph.MicOff, ink, "Mute microphone", "Toggles the mic channel", "Ctrl+Shift+M", false));
        global.Children.Add(ShortcutRow(Glyph.Game, ink, "Toggle mini-player", "Shows or hides Gaming Mode", "Ctrl+Shift+G", false));
        global.Children.Add(ShortcutRow(Glyph.Mic, ink, "Push-to-talk", "Hold to send your microphone", "V", true));
        root.Children.Add(Card(global, new Thickness(20, 4, 20, 4)));

        Content = Scroll(root);
    }

    private static Border ShortcutRow(string glyph, Brush tint, string name, string detail, string combo, bool last)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var k in combo.Split('+'))
        {
            keys.Children.Add(new Border
            {
                Background = (Brush)Application.Current.FindResource("RaisedBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                Child = Text(k, 12, weight: FontWeights.SemiBold),
            });
        }
        var change = PageKit.Button("Change", "GhostButton", enabled: false, tooltip: "Assigning shortcuts arrives in Iteration 2.");
        change.Margin = new Thickness(12, 0, 0, 0);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(keys);
        right.Children.Add(change);

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icon(glyph, 18);
        icon.Foreground = tint;
        icon.Width = 28;
        title.Children.Add(icon);
        var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        texts.Children.Add(Text(name, 15));
        texts.Children.Add(Text(detail, 12, muted: true));
        title.Children.Add(texts);

        var dock = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(title);
        return new Border
        {
            Child = dock,
            BorderBrush = (Brush)Application.Current.FindResource("LineBrush"),
            BorderThickness = last ? new Thickness(0) : new Thickness(0, 0, 0, 1),
        };
    }
}
