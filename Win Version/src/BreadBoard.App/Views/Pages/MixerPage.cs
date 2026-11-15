using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using BreadBoard.App.Views.Dialogs;
using BreadBoard.Audio;
using BreadBoard.Core.Config;
using BreadBoard.Core.Models;
using static BreadBoard.App.Views.Pages.PageKit;

namespace BreadBoard.App.Views.Pages;

/// <summary>
/// Mixer: four channel strips, processing and devices. The App Audio strip is live (Iteration 3);
/// microphone, soundboard/master faders, processing and devices go live in Iteration 4 and say so.
/// </summary>
public sealed class MixerPage : UserControl
{
    private readonly SoundboardController _sb;

    public MixerPage(ConfigService config, SoundboardController sb)
    {
        _sb = sb;
        var c = config.Current;
        var root = new StackPanel();
        root.Children.Add(Header("Mixer", "Microphone, an app of your choice and the soundboard, mixed into one virtual microphone."));
        root.Children.Add(Pending("App Audio is live · microphone, master, processing and devices go live in Iteration 4"));

        var strips = new UniformGrid { Rows = 1, Margin = new Thickness(0, 16, 0, 0) };
        strips.Children.Add(Strip("Microphone", Glyph.Mic, "Physical input", c.Channels.Mic, null));
        strips.Children.Add(AppStrip(c.Channels.App));
        strips.Children.Add(Strip("Soundboard", Glyph.Grid, "Your sounds", c.Channels.Soundboard, null));
        strips.Children.Add(Strip("Master", Glyph.Waveform, "To the virtual mic", c.Channels.Master, null, master: true));
        root.Children.Add(strips);

        root.Children.Add(SectionTitle("Processing"));
        var proc = new UniformGrid { Rows = 1 };
        var p = c.Processing;
        proc.Children.Add(Processor("Noise gate", "Silences the mic below a threshold", p.NoiseGate.Enabled, p.NoiseGate.Value, "%"));
        proc.Children.Add(Processor("Compressor", "Evens out loud peaks", p.Compressor.Enabled, p.Compressor.Value, "%"));
        proc.Children.Add(Processor("Limiter", "Ceiling on the master output", p.Limiter.Enabled, p.Limiter.Value, "%"));
        proc.Children.Add(Processor("Voice ducking", "Lowers app audio while you talk", p.Ducking.Enabled, p.Ducking.Value, "%"));
        root.Children.Add(proc);

        root.Children.Add(SectionTitle("Devices"));
        var devices = new UniformGrid { Rows = 1 };
        devices.Children.Add(DeviceField("Microphone input", "System default microphone"));
        devices.Children.Add(DeviceField("Virtual mic output", "VB-Audio Virtual Cable (auto-detected)"));
        devices.Children.Add(DeviceField("Monitor (your headphones)", "Off"));
        root.Children.Add(Card(devices, new Thickness(20, 16, 20, 18)));

        Content = Scroll(root);
    }

    private static Border Strip(string name, string glyph, string desc, ChannelSettings s, FrameworkElement? extra, bool master = false)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Icon(glyph, 18, "Accent2Brush"));
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        titles.Children.Add(Text(name, 15, weight: FontWeights.SemiBold));
        titles.Children.Add(Text(desc, 12, muted: true));
        head.Children.Add(titles);
        p.Children.Add(head);
        // Same-height slot in every strip so the faders line up even though only App Audio has a picker.
        var slot = extra ?? new Border();
        slot.Height = 40;
        slot.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(slot);

        var faderRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        faderRow.Children.Add(new Slider
        {
            Orientation = Orientation.Vertical, Height = 170, Minimum = 0, Maximum = 200, Value = s.Gain,
            IsEnabled = false, ToolTip = "Gain control goes live in Iteration 4.",
        });
        faderRow.Children.Add(new ProgressBar
        {
            Style = (Style)Application.Current.FindResource("Meter"), Orientation = Orientation.Vertical,
            Width = 6, Height = 170, Value = 0, Margin = new Thickness(14, 0, 0, 0), ToolTip = "Level meter (Iteration 4)",
        });
        p.Children.Add(faderRow);
        var value = Text($"{s.Gain}%", 22, weight: FontWeights.SemiBold);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.Margin = new Thickness(0, 12, 0, 12);
        p.Children.Add(value);

        var mute = PageKit.Button("Mute", glyph: master ? Glyph.Mute : name == "Microphone" ? Glyph.MicOff : Glyph.Mute, enabled: false,
            tooltip: "Mute goes live in Iteration 4.");
        mute.HorizontalAlignment = HorizontalAlignment.Stretch;
        p.Children.Add(mute);

        var routing = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (!master) routing.Children.Add(Routing("To virtual mic", s.ToVirtualMic));
        routing.Children.Add(Routing("Monitor", s.ToMonitor));
        p.Children.Add(routing);

        var card = Card(p, new Thickness(18, 18, 18, 16));
        card.Margin = new Thickness(0, 0, 14, 0);
        return card;
    }

    private static DockPanel Routing(string label, bool on)
    {
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var sw = Switch(on, false, "Routing goes live in Iteration 4.");
        DockPanel.SetDock(sw, Dock.Right);
        d.Children.Add(sw);
        d.Children.Add(Text(label, 13, muted: true));
        return d;
    }

    /// <summary>The live App Audio strip: app picker with status, gain fader, meter, mute and routing.</summary>
    private Border AppStrip(ChannelSettings s)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Icon(Glyph.App, 18, "Accent2Brush"));
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        titles.Children.Add(Text("App Audio", 15, weight: FontWeights.SemiBold));
        var status = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var statusText = Text("", 12, muted: true);
        statusText.TextWrapping = TextWrapping.NoWrap;
        statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        status.Children.Add(dot);
        status.Children.Add(statusText);
        titles.Children.Add(status);
        head.Children.Add(titles);
        p.Children.Add(head);

        // Picker button (same 40 px slot as the other strips keep empty).
        var pickIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var pickText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var pickContent = new DockPanel();
        var chevron = Icon("\uE70D", 11, "MuteBrush");
        chevron.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(pickIcon, Dock.Left);
        DockPanel.SetDock(chevron, Dock.Right);
        pickContent.Children.Add(pickIcon);
        pickContent.Children.Add(chevron);
        pickContent.Children.Add(pickText);
        var pick = new Button
        {
            Style = (Style)Application.Current.FindResource("SoftButton"),
            Content = pickContent,
            Height = 40,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Choose the app whose sound joins your virtual mic",
        };
        System.Windows.Automation.AutomationProperties.SetName(pick, "Choose app");
        pick.Click += async (_, _) => await AppPickerDialog.PickAsync(_sb);
        // The pill template stacks its content, so size it to the button to put the chevron on the right.
        pick.SizeChanged += (_, e) => pickContent.Width = Math.Max(0, e.NewSize.Width - pick.Padding.Left - pick.Padding.Right - 2);
        p.Children.Add(pick);

        var fader = new Slider { Orientation = Orientation.Vertical, Height = 170, Minimum = 0, Maximum = 200, Value = s.Gain, SmallChange = 1, LargeChange = 10, ToolTip = "App volume (100 % = as the app plays it)" };
        System.Windows.Automation.AutomationProperties.SetName(fader, "App audio volume");
        var meter = new ProgressBar
        {
            Style = (Style)Application.Current.FindResource("Meter"), Orientation = Orientation.Vertical,
            Width = 6, Height = 170, Maximum = 1, Value = 0, Margin = new Thickness(14, 0, 0, 0), ToolTip = "App audio level",
        };
        var faderRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        faderRow.Children.Add(fader);
        faderRow.Children.Add(meter);
        p.Children.Add(faderRow);
        var value = Text($"{s.Gain}%", 22, weight: FontWeights.SemiBold);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.Margin = new Thickness(0, 12, 0, 12);
        p.Children.Add(value);
        fader.ValueChanged += (_, _) =>
        {
            var g = (int)Math.Round(fader.Value);
            value.Text = $"{g}%";
            _sb.SetAppChannel(gain: g);
        };

        var mute = PageKit.Button(s.Muted ? "Unmute" : "Mute", glyph: Glyph.Mute);
        mute.HorizontalAlignment = HorizontalAlignment.Stretch;
        void ShowMute(bool m)
        {
            mute.Content = m ? "Unmute" : "Mute";
            mute.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource(m ? "BadBrush" : "InkBrush");
        }
        ShowMute(s.Muted);
        mute.Click += (_, _) =>
        {
            var m = !_sb.Config.Channels.App.Muted;
            _sb.SetAppChannel(muted: m);
            ShowMute(m);
        };
        p.Children.Add(mute);

        var routing = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var toMic = Switch(s.ToVirtualMic, true, "Send this app's sound to the virtual mic");
        toMic.Click += (_, _) => _sb.SetAppChannel(toVirtualMic: toMic.IsChecked == true);
        routing.Children.Add(RoutingRow("To virtual mic", toMic));
        routing.Children.Add(RoutingRow("Monitor", Switch(false, false, "You already hear the app itself; monitoring it too would play it twice.")));
        p.Children.Add(routing);

        void Refresh()
        {
            var sel = _sb.Config.Devices.AppSource;
            var st = _sb.AppStatus;
            pickText.Text = sel is null ? "Choose an app…" : st.Name ?? sel.DisplayName ?? sel.ProcessName;
            pickIcon.Source = AppIcons.Get(sel?.ExePath);
            pickIcon.Visibility = pickIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
            (statusText.Text, var brush) = (sel, st.State) switch
            {
                (null, _) => ("No app chosen", "MuteBrush"),
                (_, SourceState.Running) => ("Capturing", "OkBrush"),
                (_, SourceState.Starting) => ("Connecting…", "Accent2Brush"),
                (_, SourceState.Unavailable) => (st.Error ?? "Not running", "Accent2Brush"),
                (_, SourceState.Failed) => (st.Error ?? "Capture failed", "BadBrush"),
                _ => (_sb.EngineStarted ? "Off" : "Starting audio…", "MuteBrush"),
            };
            dot.Fill = (System.Windows.Media.Brush)Application.Current.FindResource(brush);
            statusText.ToolTip = statusText.Text;
        }
        _sb.EngineChanged += Refresh;
        _sb.PlaybackTick += () => meter.Value = Math.Min(1, _sb.AppLevel);
        Refresh();

        var card = Card(p, new Thickness(18, 18, 18, 16));
        card.Margin = new Thickness(0, 0, 14, 0);
        return card;
    }

    private static DockPanel RoutingRow(string label, CheckBox sw)
    {
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(sw, Dock.Right);
        d.Children.Add(sw);
        d.Children.Add(Text(label, 13, muted: true));
        return d;
    }

    private static Border Processor(string title, string desc, bool on, int value, string unit)
    {
        var p = new StackPanel();
        var top = new DockPanel();
        var sw = Switch(on, false, "Processing goes live in Iteration 4.");
        DockPanel.SetDock(sw, Dock.Right);
        top.Children.Add(sw);
        var t = new StackPanel();
        t.Children.Add(Text(title, 15, weight: FontWeights.SemiBold));
        t.Children.Add(Text(desc, 12, muted: true));
        top.Children.Add(t);
        p.Children.Add(top);
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var v = Text($"{value}{unit}", 13, muted: true);
        v.Width = 44;
        v.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(v, Dock.Right);
        row.Children.Add(v);
        row.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = value, IsEnabled = false, ToolTip = "Processing goes live in Iteration 4." });
        p.Children.Add(row);
        var card = Card(p, new Thickness(18, 16, 18, 14));
        card.Margin = new Thickness(0, 0, 14, 0);
        return card;
    }

    private static StackPanel DeviceField(string label, string value)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        p.Children.Add(Text(label, 13, weight: FontWeights.Medium));
        var box = new ComboBox { IsEnabled = false, Margin = new Thickness(0, 6, 0, 0), ToolTip = "Device selection goes live in Iteration 4." };
        box.Items.Add(value);
        box.SelectedIndex = 0;
        p.Children.Add(box);
        return p;
    }
}

