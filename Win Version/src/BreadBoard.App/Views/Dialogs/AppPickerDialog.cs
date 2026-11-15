using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using BreadBoard.Audio.Sources;
using BreadBoard.Core.Sounds;
using static BreadBoard.App.Views.Pages.PageKit;

namespace BreadBoard.App.Views.Dialogs;

/// <summary>
/// Choose the running app whose sound is mixed into the virtual mic (only that app: other apps and
/// Windows notifications stay out). Apps playing audio right now are listed first.
/// </summary>
public sealed class AppPickerDialog : DialogWindow
{
    private readonly SoundboardController _sb;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _status = Text("Looking for apps…", 13, muted: true);
    private readonly TextBlock _warning = Text("", 13);
    private readonly Button _use;
    private IReadOnlyList<AudioApp> _apps = [];
    private readonly string? _current;

    private AppPickerDialog(SoundboardController sb) : base("Choose app audio", 560)
    {
        _sb = sb;
        _current = sb.Config.Devices.AppSource?.ProcessName;
        _search.Style = Res<Style>("Field");
        _search.TextChanged += (_, _) => Fill();
        AutomationPropertiesHelper.SetName(_search, "Search apps");
        var refresh = Pages.PageKit.Button("Refresh", glyph: "");
        refresh.Margin = new Thickness(8, 6, 0, 0);
        refresh.Click += async (_, _) => await LoadAsync();
        var top = new DockPanel();
        DockPanel.SetDock(refresh, Dock.Right);
        top.Children.Add(refresh);
        var hint = Text("Search apps…", 14, muted: true);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(14, 6, 0, 0);
        _search.TextChanged += (_, _) => hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchHost = new Grid();
        searchHost.Children.Add(_search);
        searchHost.Children.Add(hint);
        top.Children.Add(searchHost);

        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.ItemContainerStyle = Res<Style>("RowItem");
        _list.Height = 340;
        _list.Margin = new Thickness(0, 10, 0, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => OnSelection();
        _list.MouseDoubleClick += (_, e) => { if (_list.SelectedItem is ListBoxItem) Accept(); };
        AutomationPropertiesHelper.SetName(_list, "Running apps");

        var body = new StackPanel();
        body.Children.Add(Text("Only the chosen app's sound goes into your virtual mic, together with your soundboard. Other apps and Windows notifications stay out.", 13, muted: true));
        top.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(top);
        body.Children.Add(_list);
        _status.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_status);
        _warning.Foreground = Res<Brush>("Accent2Brush");
        _warning.Margin = new Thickness(0, 8, 0, 0);
        _warning.Visibility = Visibility.Collapsed;
        body.Children.Add(_warning);
        Body = body;

        AddButton("Cancel");
        _use = AddButton("Use this app", primary: true, isDefault: true);
        _use.Click += (_, _) => Accept();
        Loaded += async (_, _) => { _search.Focus(); await LoadAsync(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _search.IsKeyboardFocused && _list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                ((ListBoxItem)_list.SelectedItem).Focus();
                e.Handled = true;
            }
        };
    }

    /// <summary>The chosen app; null with <see cref="ChoseNone"/> = no app audio.</summary>
    public AudioApp? Chosen { get; private set; }
    public bool ChoseNone { get; private set; }

    /// <summary>Non-modal instance for the screenshot tour.</summary>
    internal static AppPickerDialog Preview(SoundboardController sb) => new(sb);

    /// <summary>Shows the picker and applies the choice. Returns true if something changed.</summary>
    public static async Task<bool> PickAsync(SoundboardController sb)
    {
        var d = new AppPickerDialog(sb);
        if (d.ShowDialog() != true) return false;
        await sb.SelectAppAsync(d.ChoseNone ? null : d.Chosen);
        return true;
    }

    private async Task LoadAsync()
    {
        _status.Text = "Looking for apps…";
        try
        {
            _apps = await _sb.ListAppsAsync();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _apps = [];
            _status.Text = $"Couldn't list apps: {ex.Message}";
            return;
        }
        Fill();
    }

    private void Fill()
    {
        var q = _search.Text.Trim();
        var selected = (_list.SelectedItem as ListBoxItem)?.Tag as AudioApp;
        _list.Items.Clear();
        if (q.Length == 0) _list.Items.Add(Row(null));
        var shown = _apps.Where(a => q.Length == 0
            || a.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.WindowTitle.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var app in shown) _list.Items.Add(Row(app));

        // Keep the selection; otherwise preselect the current app (or "No app").
        var pick = _list.Items.Cast<ListBoxItem>().FirstOrDefault(i =>
            i.Tag is AudioApp a && (selected is not null ? a.ProcessId == selected.ProcessId
                : string.Equals(a.ProcessName, _current, StringComparison.OrdinalIgnoreCase)));
        _list.SelectedItem = pick ?? (_current is null && q.Length == 0 ? _list.Items[0] : null);

        var playing = _apps.Count(a => a.IsPlaying);
        _status.Text = _apps.Count == 0 ? "No apps found." :
            shown.Count == 0 ? "No app matches your search." :
            $"{_apps.Count} apps · {playing} playing sound right now";
        if (_current is not null && !_apps.Any(a => string.Equals(a.ProcessName, _current, StringComparison.OrdinalIgnoreCase)))
            _status.Text += $" · {_sb.Config.Devices.AppSource?.DisplayName ?? _current} isn't running (it's used when it starts)";
        OnSelection();
    }

    private static ListBoxItem Row(AudioApp? app)
    {
        var dock = new DockPanel();
        FrameworkElement icon;
        if (app is not null && AppIcons.Get(app.ExePath) is { } img)
            icon = new Image { Source = img, Width = 28, Height = 28 };
        else
        {
            var g = Icon(app is null ? "" : Glyph.App, 20, "MuteBrush");
            g.Width = 28;
            g.TextAlignment = TextAlignment.Center;
            icon = g;
        }
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);

        if (app is { IsPlaying: true } or { HasAudioSession: true })
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 2, 9, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = app.IsPlaying ? new SolidColorBrush(Color.FromArgb(0x26, 0x6F, 0xAE, 0x7A)) : Res<Brush>("RaisedBrush"),
                Child = Text(app.IsPlaying ? "Playing now" : "Has audio", 12),
            };
            if (app.IsPlaying) ((TextBlock)badge.Child).Foreground = Res<Brush>("OkBrush");
            DockPanel.SetDock(badge, Dock.Right);
            dock.Children.Add(badge);
        }

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(app is null ? "No app" : app.Label, 14, weight: FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(title);
        var sub = Text(app is null ? "Only your soundboard (and later your mic) goes to the virtual mic"
            : app.WindowTitle == app.ProcessName ? $"{app.ProcessName}.exe" : app.WindowTitle, 12, muted: true);
        sub.TextWrapping = TextWrapping.NoWrap;
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(sub);
        dock.Children.Add(texts);

        var item = new ListBoxItem { Content = dock, Tag = app };
        AutomationPropertiesHelper.SetName(item, app?.Label ?? "No app");
        return item;
    }

    private void OnSelection()
    {
        var item = _list.SelectedItem as ListBoxItem;
        var app = item?.Tag as AudioApp;
        _use.IsEnabled = item is not null;
        _use.Content = item is null ? "Use this app" : app is null ? "Use no app" : $"Use {Shorten(app.Label)}";
        var warn = app is null ? null : AppSourceRules.Warning(app.ProcessName);
        _warning.Text = warn is null ? "" : $"⚠  {warn}";
        _warning.Visibility = warn is null ? Visibility.Collapsed : Visibility.Visible;

        static string Shorten(string s) => s.Length > 22 ? s[..21] + "…" : s;
    }

    private void Accept()
    {
        if (_list.SelectedItem is not ListBoxItem item) return;
        Chosen = item.Tag as AudioApp;
        ChoseNone = Chosen is null;
        DialogResult = true;
    }
}
