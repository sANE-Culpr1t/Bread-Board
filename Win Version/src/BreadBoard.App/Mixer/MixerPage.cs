using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Mixing;
using BreadBoard.Core.Models;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using static BreadBoard.App.Theme.PageKit;

namespace BreadBoard.App.Mixer;

/// <summary>
/// The live mixer: four channel strips (microphone, chosen app, soundboard, master), voice processing,
/// and device choices. Everything applies immediately; levels are shown in dB.
/// </summary>
public sealed class MixerPage : UserControl
{
    private readonly SoundboardController _sb;
    private readonly List<Action> _refreshers = [];
    private bool _filling;
    private readonly Dictionary<DeviceKind, IReadOnlyList<AudioDeviceInfo>> _deviceCache = [];

    public MixerPage(SoundboardController sb)
    {
        _sb = sb;
        var root = new StackPanel();
        root.Children.Add(Header("Mixer", "Your microphone, an app of your choice and the soundboard, mixed into one virtual microphone."));

        var strips = new UniformGrid { Rows = 1, Margin = new Thickness(0, 4, 0, 0) };
        strips.Children.Add(Strip(MixChannel.Mic, "Microphone", Glyph.Mic, MicSlot()));
        strips.Children.Add(Strip(MixChannel.App, "App Audio", Glyph.App, AppSlot()));
        strips.Children.Add(Strip(MixChannel.Soundboard, "Soundboard", Glyph.Grid, BoardSlot()));
        strips.Children.Add(Strip(MixChannel.Master, "Master", Glyph.Waveform, MasterSlot()));
        root.Children.Add(strips);

        root.Children.Add(SectionTitle("Voice processing"));
        var proc = new UniformGrid { Rows = 1 };
        proc.Children.Add(ProcessorCard(Processor.NoiseGate, "Noise gate", "Silences the mic below this level (keyboard, fans)", 0, 30, "%"));
        proc.Children.Add(ProcessorCard(Processor.Compressor, "Compressor", "Evens out loud and quiet speech", 0, 100, "%"));
        proc.Children.Add(ProcessorCard(Processor.Limiter, "Limiter", "Ceiling on the virtual mic: no clipping", 50, 100, "%"));
        proc.Children.Add(ProcessorCard(Processor.Ducking, "Voice ducking", "Lowers app audio while you talk", 0, 100, "%"));
        root.Children.Add(proc);

        root.Children.Add(SectionTitle("Devices"));
        root.Children.Add(DevicesCard());

        Content = Scroll(root);
        _sb.EngineChanged += RefreshAll;
        _sb.MixChanged += RefreshAll;
        _sb.DevicesListChanged += () => { _deviceCache.Clear(); RefreshAll(); };
        _sb.PushToTalkChanged += _ => RefreshAll();
        RefreshAll();
    }

    private void RefreshAll()
    {
        _filling = true;
        try { foreach (var r in _refreshers) r(); }
        finally { _filling = false; }
    }

    private static Brush Res(string key) => Res<Brush>(key);

    private Border Strip(MixChannel ch, string title, string glyph, FrameworkElement slot)
    {
        var s = SoundboardController.Settings(_sb.Config, ch);
        var p = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Icon(glyph, 18, "Accent2Brush"));
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        titles.Children.Add(Text(title, 15, weight: FontWeights.SemiBold));
        var status = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var statusText = Text("", 12, muted: true);
        statusText.TextWrapping = TextWrapping.NoWrap;
        statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        statusText.MaxWidth = 190;
        status.Children.Add(dot);
        status.Children.Add(statusText);
        titles.Children.Add(status);
        head.Children.Add(titles);
        p.Children.Add(head);

        slot.Height = 40;
        slot.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(slot);

        var fader = new Slider { Orientation = Orientation.Vertical, Height = 170, Minimum = 0, Maximum = ChannelSettings.MaxGain, Value = s.Gain, SmallChange = 1, LargeChange = 10, ToolTip = "Volume (100 % = unchanged, above 100 % boosts)" };
        System.Windows.Automation.AutomationProperties.SetName(fader, $"{title} volume");
        var meter = new ProgressBar
        {
            Style = Res<Style>("Meter"), Orientation = Orientation.Vertical,
            Width = 6, Height = 170, Maximum = 1, Value = 0, Margin = new Thickness(14, 0, 0, 0), ToolTip = $"{title} level",
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
            if (!_filling) _sb.SetChannel(ch, gain: g);
        };

        var mute = PageKit.Button("Mute", glyph: ch == MixChannel.Mic ? Glyph.MicOff : Glyph.Mute);
        mute.HorizontalAlignment = HorizontalAlignment.Stretch;
        System.Windows.Automation.AutomationProperties.SetName(mute, $"Mute {title}");
        mute.Click += (_, _) => _sb.SetChannel(ch, muted: !SoundboardController.Settings(_sb.Config, ch).Muted);
        p.Children.Add(mute);

        var routing = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        CheckBox? toMic = null, toMon = null;
        if (ch != MixChannel.Master)
        {
            toMic = Switch(s.ToVirtualMic, true, "Send this channel to the virtual mic (what others hear)");
            OnToggle(toMic, () => SoundboardController.Settings(_sb.Config, ch).ToVirtualMic, on => _sb.SetChannel(ch, toVirtualMic: on));
            routing.Children.Add(RoutingRow("To virtual mic", toMic));
            var canMonitor = SoundboardController.CanMonitor(ch);
            toMon = canMonitor
                ? Switch(s.ToMonitor, true, ch == MixChannel.Mic ? "Hear your own voice in your headphones" : "Hear your sounds in your headphones")
                : Switch(false, false, "You already hear the app itself; monitoring it would play it twice.");
            if (canMonitor) OnToggle(toMon, () => SoundboardController.Settings(_sb.Config, ch).ToMonitor, on => _sb.SetChannel(ch, toMonitor: on));
            routing.Children.Add(RoutingRow("Monitor", toMon));
        }
        else
        {
            routing.Children.Add(Text("Limiter and the virtual-mic switch apply here.", 12, muted: true));
        }
        p.Children.Add(routing);

        _refreshers.Add(() =>
        {
            var cur = SoundboardController.Settings(_sb.Config, ch);
            if ((int)Math.Round(fader.Value) != cur.Gain) fader.Value = cur.Gain;
            mute.Content = cur.Muted ? "Unmute" : "Mute";
            mute.Foreground = Res(cur.Muted ? "BadBrush" : "InkBrush");
            if (toMic is not null) toMic.IsChecked = cur.ToVirtualMic;
            if (toMon is not null && SoundboardController.CanMonitor(ch)) toMon.IsChecked = cur.ToMonitor;
            var (text, brush) = ChannelStatus(ch, cur);
            statusText.Text = text;
            statusText.ToolTip = text;
            dot.Fill = Res(brush);
        });
        _sb.PlaybackTick += () => meter.Value = MeterScale.Of(ch switch
        {
            MixChannel.Mic => _sb.Levels.Mic,
            MixChannel.App => _sb.Levels.App,
            MixChannel.Soundboard => _sb.Levels.Soundboard,
            _ => _sb.Levels.Master,
        });

        var card = Card(p, new Thickness(18, 18, 18, 16));
        card.Margin = new Thickness(0, 0, 14, 0);
        return card;
    }

    private (string Text, string Brush) ChannelStatus(MixChannel ch, ChannelSettings s)
    {
        var st = _sb.Status;
        if (s.Muted) return ("Muted", "BadBrush");
        switch (ch)
        {
            case MixChannel.Mic:
                if (!_sb.Config.Devices.MicEnabled) return ("Off", "MuteBrush");
                if (st.Mic.State == SourceState.Running)
                {
                    if (_sb.Config.Processing.PushToTalk.Enabled)
                        return _sb.PushToTalkDown ? ("Talking (push-to-talk)", "OkBrush") : ($"Hold {_sb.Config.Processing.PushToTalk.Key} to talk", "Accent2Brush");
                    return (st.Mic.Name ?? "Live", "OkBrush");
                }
                return (st.Mic.Error ?? (_sb.EngineStarted ? "Starting…" : "Off"), st.Mic.State == SourceState.Failed ? "BadBrush" : "Accent2Brush");
            case MixChannel.App:
                return (_sb.Config.Devices.AppSource, st.App.State) switch
                {
                    (null, _) => ("No app chosen", "MuteBrush"),
                    (_, SourceState.Running) => ("Capturing", "OkBrush"),
                    (_, SourceState.Starting) => ("Connecting…", "Accent2Brush"),
                    (_, SourceState.Unavailable) => (st.App.Error ?? "Not running", "Accent2Brush"),
                    (_, SourceState.Failed) => (st.App.Error ?? "Capture failed", "BadBrush"),
                    _ => ("Starting…", "MuteBrush"),
                };
            case MixChannel.Soundboard:
                var n = _sb.Items.Count;
                return (n == 0 ? "No sounds yet" : $"{n} sound{(n == 1 ? "" : "s")}", n == 0 ? "MuteBrush" : "OkBrush");
            default:
                if (!_sb.Config.Devices.VirtualMicEnabled) return ("Virtual mic off (sending silence)", "Accent2Brush");
                return st.Output.State == SourceState.Running ? ("Live on the virtual mic", "OkBrush")
                    : (st.Output.Error ?? "No virtual mic output", "BadBrush");
        }
    }

    private static DockPanel RoutingRow(string label, CheckBox sw)
    {
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(sw, Dock.Right);
        d.Children.Add(sw);
        d.Children.Add(Text(label, 13, muted: true));
        return d;
    }

    private static DockPanel SwitchRow(string label, CheckBox sw)
    {
        var d = RoutingRow(label, sw);
        d.Margin = new Thickness(4, 0, 0, 0);
        d.VerticalAlignment = VerticalAlignment.Center;
        return d;
    }

    private DockPanel MicSlot()
    {
        var ptt = Switch(_sb.Config.Processing.PushToTalk.Enabled, true, "Only send your mic while the push-to-talk key is held (set the key on the Shortcuts page). The key still works in other apps.");
        System.Windows.Automation.AutomationProperties.SetName(ptt, "Push-to-talk");
        OnToggle(ptt, () => _sb.Config.Processing.PushToTalk.Enabled, async on => await _sb.SetPushToTalkAsync(on));
        _refreshers.Add(() => ptt.IsChecked = _sb.Config.Processing.PushToTalk.Enabled);
        return SwitchRow("Push-to-talk", ptt);
    }

    private DockPanel MasterSlot()
    {
        var vm = Switch(_sb.Config.Devices.VirtualMicEnabled, true, "Off sends silence to the virtual mic (meters keep working)");
        System.Windows.Automation.AutomationProperties.SetName(vm, "Virtual mic on");
        OnToggle(vm, () => _sb.Config.Devices.VirtualMicEnabled, _sb.SetVirtualMic);
        _refreshers.Add(() => vm.IsChecked = _sb.Config.Devices.VirtualMicEnabled);
        return SwitchRow("Virtual mic on", vm);
    }

    private Button BoardSlot()
    {
        var stop = PageKit.Button("Stop all", glyph: Glyph.Stop, tooltip: "Stop every sound that is playing");
        stop.Click += (_, _) => _sb.StopAll();
        return stop;
    }

    private Button AppSlot()
    {
        var pickIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var pickText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var content = new DockPanel();
        var chevron = Icon("", 11, "MuteBrush");
        chevron.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(pickIcon, Dock.Left);
        DockPanel.SetDock(chevron, Dock.Right);
        content.Children.Add(pickIcon);
        content.Children.Add(chevron);
        content.Children.Add(pickText);
        var pick = new Button
        {
            Style = Res<Style>("SoftButton"),
            Content = content,
            ToolTip = "Choose the app whose sound joins your virtual mic",
        };
        System.Windows.Automation.AutomationProperties.SetName(pick, "Choose app");
        pick.Click += async (_, _) => await AppPickerDialog.PickAsync(_sb);
        // The pill template stacks its content, so size it to the button to put the chevron on the right.
        pick.SizeChanged += (_, e) => content.Width = Math.Max(0, e.NewSize.Width - pick.Padding.Left - pick.Padding.Right - 2);
        _refreshers.Add(() =>
        {
            var sel = _sb.Config.Devices.AppSource;
            pickText.Text = sel is null ? "Choose an app…" : _sb.Status.App.Name ?? sel.DisplayName ?? sel.ProcessName;
            pickIcon.Source = AppIcons.Get(sel?.ExePath);
            pickIcon.Visibility = pickIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
        });
        return pick;
    }

    private Border ProcessorCard(Processor proc, string title, string desc, int min, int max, string unit)
    {
        var s = SoundboardController.Settings(_sb.Config, proc);
        var p = new StackPanel();
        var top = new DockPanel();
        var sw = Switch(s.Enabled, true, $"Turn the {title.ToLowerInvariant()} on or off");
        System.Windows.Automation.AutomationProperties.SetName(sw, title);
        OnToggle(sw, () => SoundboardController.Settings(_sb.Config, proc).Enabled, on => _sb.SetProcessing(proc, enabled: on));
        DockPanel.SetDock(sw, Dock.Right);
        top.Children.Add(sw);
        var t = new StackPanel();
        t.Children.Add(Text(title, 15, weight: FontWeights.SemiBold));
        t.Children.Add(Text(desc, 12, muted: true));
        top.Children.Add(t);
        p.Children.Add(top);
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var v = Text($"{s.Value}{unit}", 13, muted: true);
        v.Width = 44;
        v.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(v, Dock.Right);
        row.Children.Add(v);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(s.Value, min, max), IsSnapToTickEnabled = true, TickFrequency = 1 };
        System.Windows.Automation.AutomationProperties.SetName(slider, $"{title} amount");
        slider.ValueChanged += (_, _) =>
        {
            var val = (int)slider.Value;
            v.Text = $"{val}{unit}";
            if (!_filling) _sb.SetProcessing(proc, value: val);
        };
        row.Children.Add(slider);
        p.Children.Add(row);
        _refreshers.Add(() =>
        {
            var cur = SoundboardController.Settings(_sb.Config, proc);
            sw.IsChecked = cur.Enabled;
            slider.IsEnabled = cur.Enabled;
        });
        var card = Card(p, new Thickness(18, 16, 18, 14));
        card.Margin = new Thickness(0, 0, 14, 0);
        return card;
    }

    private Border DevicesCard()
    {
        var grid = new UniformGrid { Rows = 1 };
        grid.Children.Add(DeviceColumn("Microphone", DeviceKind.Input, () => _sb.Config.Devices.InputDeviceId,
            id => _sb.SetDevicesAsync(d => d.InputDeviceId = id), "System default microphone",
            () => _sb.Config.Devices.MicEnabled, on => _sb.SetDevicesAsync(d => d.MicEnabled = on), "Use microphone",
            () => _sb.Status.Mic, info => info.IsVirtualCable ? " (virtual cable: would echo)" : ""));
        grid.Children.Add(DeviceColumn("Virtual mic output", DeviceKind.Output, () => _sb.Config.Devices.OutputDeviceId,
            id => _sb.SetDevicesAsync(d => d.OutputDeviceId = id), "Auto: VB-Audio virtual cable",
            null, null, null, () => _sb.Status.Output, info => info.IsVirtualCable ? " (recommended)" : ""));
        grid.Children.Add(DeviceColumn("Monitor (your headphones)", DeviceKind.Output, () => _sb.Config.Devices.MonitorDeviceId,
            id => _sb.SetDevicesAsync(d => d.MonitorDeviceId = id), "System default speakers",
            () => _sb.Config.Devices.MonitorEnabled, on => _sb.SetDevicesAsync(d => d.MonitorEnabled = on), "Monitor on",
            () => _sb.Status.Monitor, info => info.IsVirtualCable ? " (virtual cable: not useful)" : ""));
        grid.Children.Add(LatencyColumn());
        return Card(grid, new Thickness(20, 16, 4, 18));
    }

    private StackPanel DeviceColumn(string label, DeviceKind kind, Func<string?> current, Func<string?, Task> choose, string defaultText,
        Func<bool>? isOn, Func<bool, Task>? setOn, string? onLabel, Func<SourceStatus> status, Func<AudioDeviceInfo, string> note)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var head = new DockPanel { Height = 30 }; // same height with or without a switch, so columns line up
        CheckBox? sw = null;
        if (isOn is not null && setOn is not null)
        {
            sw = Switch(isOn(), true, onLabel ?? label);
            System.Windows.Automation.AutomationProperties.SetName(sw, onLabel ?? label);
            OnToggle(sw, isOn, async on => await setOn(on));
            DockPanel.SetDock(sw, Dock.Right);
            head.Children.Add(sw);
        }
        head.Children.Add(Text(label, 13, weight: FontWeights.Medium));
        p.Children.Add(head);
        var box = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        box.SelectionChanged += async (_, _) =>
        {
            if (_filling || box.SelectedItem is not ComboBoxItem item) return;
            await choose(item.Tag as string);
        };
        p.Children.Add(box);
        var st = Text("", 12, muted: true);
        st.Margin = new Thickness(2, 6, 0, 0);
        st.TextWrapping = TextWrapping.Wrap;
        p.Children.Add(st);

        _refreshers.Add(() =>
        {
            if (sw is not null) sw.IsChecked = isOn!();
            box.Items.Clear();
            box.Items.Add(new ComboBoxItem { Content = defaultText, Tag = null });
            IReadOnlyList<AudioDeviceInfo> devices = [];
            if (_deviceCache.TryGetValue(kind, out var cached)) devices = cached;
            else if (_sb.EngineStarted) devices = _deviceCache[kind] = _sb.ListDevices(kind);
            foreach (var d in devices)
                box.Items.Add(new ComboBoxItem { Content = d.Name + note(d), Tag = d.Id });
            var id = current();
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == id) ?? box.Items[0];
            box.IsEnabled = isOn?.Invoke() ?? true;
            var s = status();
            (st.Text, st.Foreground) = s.State switch
            {
                SourceState.Running => ($"In use: {s.Name}", Res("OkBrush")),
                SourceState.Failed => (s.Error ?? "Failed", Res("BadBrush")),
                SourceState.Unavailable => (s.Error ?? "Not available", Res("Accent2Brush")),
                SourceState.Starting => ("Starting…", Res("MuteBrush")),
                _ => (isOn?.Invoke() == false ? "Off" : _sb.EngineStarted ? "Off" : "Starts with the audio engine", Res("MuteBrush")),
            };
        });
        return p;
    }

    private StackPanel LatencyColumn()
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var title = Text("Buffer size", 13, weight: FontWeights.Medium);
        title.Height = 30;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Padding = new Thickness(0, 6, 0, 0);
        p.Children.Add(title);
        var box = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(box, "Buffer size");
        foreach (var (ms, label) in DeviceSettings.LatencyChoices)
            box.Items.Add(new ComboBoxItem { Content = label.Length == 0 ? $"{ms} ms" : $"{ms} ms ({label})", Tag = ms });
        box.SelectionChanged += async (_, _) =>
        {
            if (_filling || box.SelectedItem is not ComboBoxItem { Tag: int ms }) return;
            await _sb.SetDevicesAsync(d => d.LatencyMs = ms);
        };
        p.Children.Add(box);
        var hint = Text("Raise it if you hear crackles; lower it for less delay.", 12, muted: true);
        hint.Margin = new Thickness(2, 6, 0, 0);
        p.Children.Add(hint);
        _refreshers.Add(() => box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == _sb.Config.Devices.LatencyMs));
        return p;
    }
}
