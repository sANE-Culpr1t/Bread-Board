using System.Windows;
using System.Windows.Controls;
using BreadBoard.App.Navigation;

namespace BreadBoard.App.Views;

/// <summary>
/// Honest stand-in for a page that hasn't been built yet: a heading and which iteration builds it.
/// It has no controls, so nothing pretends to work.
/// </summary>
public sealed class PlaceholderPage : StackPanel
{
    private static readonly Dictionary<PageId, string> Plan = new()
    {
        [PageId.Soundboard] = "Sound pads, search and playback arrive in Iterations 1–2.",
        [PageId.WebPlayer] = "The browser and browser-audio capture arrive in Iteration 3. WebView2 is not loaded until then.",
        [PageId.Mixer] = "Channel faders, meters and the virtual microphone arrive in Iterations 1 and 4.",
        [PageId.Shortcuts] = "Global shortcut assignment arrives in Iteration 2.",
        [PageId.Settings] = "Devices, profiles and preferences arrive in Iterations 1, 4 and 5.",
    };

    public PlaceholderPage(string title, PageId id)
    {
        Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold });
        Children.Add(new TextBlock
        {
            Text = Plan[id],
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("MuteBrush"),
        });
    }
}
