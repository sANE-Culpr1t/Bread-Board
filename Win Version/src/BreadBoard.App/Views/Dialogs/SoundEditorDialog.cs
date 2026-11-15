using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BreadBoard.App.Controls;
using BreadBoard.App.Services;
using BreadBoard.App.ViewModels;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;
using Microsoft.Win32;
using static BreadBoard.App.Views.Pages.PageKit;

namespace BreadBoard.App.Views.Dialogs;

/// <summary>
/// Add Sound (from a file or a website link) and Edit Sound: name, category, shortcut, play mode,
/// volume, icon, colour and favourite. Global shortcuts are paused while it is open.
/// </summary>
public sealed class SoundEditorDialog : DialogWindow
{
    private readonly SoundboardController _sb;
    private readonly SoundEntry? _entry; // null = adding
    private readonly TextBox _name = new() { MaxLength = SoundLibrary.MaxNameLength };
    private readonly ComboBox _category = new() { IsEditable = true };
    private readonly ComboBox _mode = new();
    private readonly ComboBox _route = new();
    private readonly HotkeyBox _hotkey = new();
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly CheckBox _favorite;
    private readonly TextBlock _error = Text("", 13);
    private readonly StackPanel _icons = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _tints = new() { Orientation = Orientation.Horizontal };
    private string _icon = "volume", _tint = "beige";

    // Add mode only
    private readonly RadioButton _fromFile = new() { Content = "From file", GroupName = "src", IsChecked = true };
    private readonly RadioButton _fromWeb = new() { Content = "From website", GroupName = "src" };
    private readonly TextBox _file = new() { IsReadOnly = true };
    private readonly TextBox _link = new();
    private readonly Button _primary;
    private bool _nameTouched;

    private SoundEditorDialog(SoundboardController sb, SoundEntry? entry, string? presetFile)
        : base(entry is null ? "Add sound" : "Edit sound", 520)
    {
        _sb = sb;
        _entry = entry;
        _favorite = Switch(entry?.Favorite ?? false, true, "Show in Favourites");
        foreach (var tb in new[] { _name, _file, _link }) tb.Style = Res<Style>("Field");
        _error.Foreground = Res<Brush>("BadBrush");
        _error.Visibility = Visibility.Collapsed;

        var body = new StackPanel();
        if (entry is null) body.Children.Add(SourceSection(presetFile));

        body.Children.Add(Field("Name", _name));
        _name.TextChanged += (_, _) => { if (_name.IsKeyboardFocused) _nameTouched = true; };

        var two = new Grid();
        two.ColumnDefinitions.Add(new ColumnDefinition());
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        two.ColumnDefinitions.Add(new ColumnDefinition());
        
        var cats = sb.Library.Categories();
        foreach (var c in cats) _category.Items.Add(c);
        _category.Text = entry?.Category ?? (cats.Count > 0 ? cats[0] : "General");
        var catField = Field("Category", _category, "Pick one or type a new name");
        two.Children.Add(catField);
        foreach (var m in Enum.GetValues<PlayMode>()) _mode.Items.Add(new ComboBoxItem { Content = SoundItem.ModeLabel(m), Tag = m });
        _mode.SelectedIndex = (int)(entry?.Mode ?? PlayMode.OneShot);
        var modeHint = Text(ModeHint(entry?.Mode ?? PlayMode.OneShot), 12, muted: true);
        _mode.SelectionChanged += (_, _) => modeHint.Text = ModeHint(SelectedMode);
        var modeField = Field("Play mode", _mode);
        modeHint.Margin = new Thickness(0, 4, 0, 0);
        modeField.Children.Add(modeHint);
        Grid.SetColumn(modeField, 2);
        two.Children.Add(modeField);
        body.Children.Add(two);

        foreach (var r in Enum.GetValues<SoundRoute>()) _route.Items.Add(new ComboBoxItem { Content = SoundItem.RouteLabel(r), Tag = r });
        _route.SelectedIndex = (int)(entry?.Route ?? SoundRoute.Both);
        System.Windows.Automation.AutomationProperties.SetName(_route, "Plays on");
        body.Children.Add(Field("Plays on", _route, "“Virtual mic only” plays for others without you hearing it; “Headphones only” lets you preview privately."));

        _hotkey.Combo = entry?.Hotkey;
        _hotkey.ComboChanged += _ => ValidateHotkey();
        var clear = Pages.PageKit.Button("Clear", "GhostButton");
        clear.Margin = new Thickness(8, 6, 0, 0);
        clear.Click += (_, _) => _hotkey.Combo = null;
        var hkRow = new DockPanel();
        DockPanel.SetDock(clear, Dock.Right);
        hkRow.Children.Add(clear);
        hkRow.Children.Add(_hotkey);
        body.Children.Add(Field("Shortcut", hkRow, "Works everywhere, even while a game has focus. Backspace clears."));

        _volume.Value = entry?.Volume ?? 85;
        var volText = Text($"{_volume.Value:0}%", 13, muted: true);
        volText.Width = 44;
        volText.TextAlignment = TextAlignment.Right;
        _volume.ValueChanged += (_, _) => volText.Text = $"{_volume.Value:0}%";
        var volRow = new DockPanel();
        DockPanel.SetDock(volText, Dock.Right);
        volRow.Children.Add(volText);
        volRow.Children.Add(_volume);
        body.Children.Add(Field("Volume", volRow));

        _icon = entry?.Icon ?? "volume";
        _tint = entry?.Tint ?? "beige";
        BuildLookPickers();
        var look = new StackPanel();
        look.Children.Add(_icons);
        _tints.Margin = new Thickness(0, 8, 0, 0);
        look.Children.Add(_tints);
        body.Children.Add(Field("Look", look));

        body.Children.Add(Row("Favourite", "Pinned to the Favourites filter", _favorite, last: true));
        _error.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(_error);
        Body = body;

        _name.Text = entry?.Name ?? "";
        AddButton("Cancel");
        _primary = AddButton(entry is null ? "Add sound" : "Save", primary: true, isDefault: true);
        _primary.Click += async (_, _) => await SubmitAsync();
        Loaded += (_, _) => (entry is null && presetFile is null ? (Control)_fromFile : _name).Focus();
    }

    public SoundItem? Result { get; private set; }

    /// <summary>Opens the Add Sound dialog; <paramref name="file"/> pre-fills the file source.</summary>
    public static SoundItem? Add(SoundboardController sb, string? file = null) => Show(new SoundEditorDialog(sb, null, file), sb);

    /// <summary>Non-modal instance for the screenshot tour.</summary>
    internal static SoundEditorDialog Preview(SoundboardController sb, string? id) => new(sb, id is null ? null : sb.Library.Find(id), null);

    public static SoundItem? Edit(SoundboardController sb, string id) =>
        sb.Library.Find(id) is { } e ? Show(new SoundEditorDialog(sb, e, null), sb) : null;

    private static SoundItem? Show(SoundEditorDialog d, SoundboardController sb)
    {
        using (sb.SuspendHotkeys()) d.ShowDialog();
        return d.Result;
    }

    private PlayMode SelectedMode => (PlayMode)((ComboBoxItem)_mode.SelectedItem).Tag;

    private static string ModeHint(PlayMode m) => m switch
    {
        PlayMode.OneShot => "Plays once; pressing again plays another copy",
        PlayMode.Toggle => "Press to start, press again to stop",
        PlayMode.Loop => "Repeats until you press it again",
        PlayMode.Hold => "Plays while the key or mouse is held",
        _ => "",
    };

    private StackPanel SourceSection(string? presetFile)
    {
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var rb in new[] { _fromFile, _fromWeb })
        {
            rb.Style = Res<Style>("Chip");
            chips.Children.Add(rb);
        }

        var browse = Pages.PageKit.Button("Browse…");
        browse.Margin = new Thickness(8, 6, 0, 0);
        browse.Click += (_, _) => PickFile();
        var fileRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        fileRow.Children.Add(browse);
        fileRow.Children.Add(_file);
        var filePanel = Field("Audio file", fileRow, "WAV, MP3 or OGG, up to 3 minutes. A copy is kept in your sound library.");
        var webPanel = Field("Link", _link, "A Myinstants sound page (myinstants.com/…/instant/…) or a direct .mp3 / .wav / .ogg link.");
        webPanel.Visibility = Visibility.Collapsed;
        _fromFile.Checked += (_, _) => { filePanel.Visibility = Visibility.Visible; webPanel.Visibility = Visibility.Collapsed; HideError(); };
        _fromWeb.Checked += (_, _) => { filePanel.Visibility = Visibility.Collapsed; webPanel.Visibility = Visibility.Visible; HideError(); _link.Focus(); };
        _link.TextChanged += (_, _) =>
        {
            if (!_nameTouched && SoundLinkParser.TryParse(_link.Text, out var l, out _)) _name.Text = l!.SuggestedName;
        };

        if (presetFile is not null) SetFile(presetFile);
        var p = new StackPanel();
        chips.Margin = new Thickness(0, 0, 0, 14);
        p.Children.Add(chips);
        p.Children.Add(filePanel);
        p.Children.Add(webPanel);
        return p;
    }

    private void PickFile()
    {
        var dlg = new OpenFileDialog { Filter = "Audio (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg", Title = "Choose a sound" };
        if (dlg.ShowDialog(this) == true) SetFile(dlg.FileName);
    }

    private void SetFile(string path)
    {
        _file.Text = path;
        if (!_nameTouched) _name.Text = SoundLibrary.NameFromFile(path);
        HideError();
    }

    private void BuildLookPickers()
    {
        _icons.Children.Clear();
        foreach (var (key, glyph) in SoundLook.Icons)
        {
            var b = new ToggleButton { Content = glyph, Style = Res<Style>("IconToggle"), Width = 34, Height = 34, FontSize = 15, Margin = new Thickness(0, 0, 6, 0), IsChecked = key == _icon, ToolTip = key };
            b.Click += (_, _) => { _icon = key; BuildLookPickers(); };
            _icons.Children.Add(b);
        }
        _tints.Children.Clear();
        foreach (var (key, _) in SoundLook.Tints)
        {
            var sel = key == _tint;
            var sw = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = key,
                Template = SwatchTemplate(SoundLook.Brush(key), sel),
            };
            sw.Click += (_, _) => { _tint = key; BuildLookPickers(); };
            _tints.Children.Add(sw);
        }
    }

    private static ControlTemplate SwatchTemplate(Brush fill, bool selected)
    {
        var outer = new FrameworkElementFactory(typeof(Border));
        outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        outer.SetValue(Border.BorderThicknessProperty, new Thickness(selected ? 2 : 0));
        outer.SetValue(Border.BorderBrushProperty, Res<Brush>("InkBrush"));
        outer.SetValue(Border.PaddingProperty, new Thickness(selected ? 3 : 0));
        var inner = new FrameworkElementFactory(typeof(Border));
        inner.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        inner.SetValue(Border.BackgroundProperty, fill);
        outer.AppendChild(inner);
        return new ControlTemplate(typeof(Button)) { VisualTree = outer };
    }

    private string? ValidateHotkey()
    {
        var combo = _hotkey.Combo;
        var why = combo is null ? null : HotkeyRules.Validate(_sb.Config, combo, _entry is null ? "sound:new" : $"sound:{_entry.Id}");
        if (why is null) HideError(); else ShowError(why);
        return why;
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }

    private void HideError() => _error.Visibility = Visibility.Collapsed;

    private async Task SubmitAsync()
    {
        if (ValidateHotkey() is not null) return;
        var name = _name.Text.Trim();
        var category = _category.Text.Trim();
        var edit = new SoundEdit(name, category.Length == 0 ? "General" : category, _hotkey.Combo, SelectedMode,
            (int)_volume.Value, _favorite.IsChecked == true, _icon, _tint, (SoundRoute)((ComboBoxItem)_route.SelectedItem).Tag);

        if (_entry is not null)
        {
            if (name.Length == 0) { ShowError("Give the sound a name."); return; }
            await _sb.SaveAsync(_entry.Id, edit);
            Result = _sb.Get(_entry.Id);
            DialogResult = true;
            return;
        }

        _primary.IsEnabled = false;
        _primary.Content = "Adding…";
        try
        {
            SoundItem? item;
            if (_fromFile.IsChecked == true)
            {
                if (!File.Exists(_file.Text)) { ShowError("Choose an audio file first."); return; }
                item = await _sb.ImportFilesAsync([_file.Text], edit.Category) is [var first, ..] ? first : null;
            }
            else
            {
                if (!SoundLinkParser.TryParse(_link.Text, out _, out var linkError)) { ShowError(linkError!); return; }
                item = await _sb.ImportLinkAsync(_link.Text, name, edit.Category);
            }
            if (item is null) { ShowError("That file couldn't be added (see the message in the main window)."); return; }
            await _sb.SaveAsync(item.Id, edit with { Name = name.Length == 0 ? item.Name : name });
            Result = item;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException or IOException or TaskCanceledException)
        {
            ShowError(ex is TaskCanceledException ? "The website took too long to answer." : ex.Message);
        }
        finally
        {
            if (IsLoaded && DialogResult is null)
            {
                _primary.IsEnabled = true;
                _primary.Content = "Add sound";
            }
        }
    }
}
