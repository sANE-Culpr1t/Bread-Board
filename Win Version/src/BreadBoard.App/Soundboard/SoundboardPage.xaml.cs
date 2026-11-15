using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BreadBoard.Core.Sounds;
using Microsoft.Win32;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;

namespace BreadBoard.App.Soundboard;

/// <summary>
/// The soundboard: pads for every sound in the library. Click (or Enter/Space) plays; hold-mode
/// sounds play while the button is held. Right-click for edit / favourite / locate / remove.
/// Files dropped on the page are imported.
/// </summary>
public partial class SoundboardPage : UserControl
{
    private const string All = "All", Favourites = "Favourites", Recent = "Recent", Missing = "Missing files";
    private readonly SoundboardController _sb;
    private readonly ListCollectionView _view;
    private string _query = "";
    private string _filter = All;
    private SoundItem? _pressed;
    private readonly List<SoundItem> _selectionOrder = [];

    public SoundboardPage(SoundboardController sb)
    {
        _sb = sb;
        InitializeComponent();
        Pads.ItemsSource = sb.Items;
        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(sb.Items);
        _view.Filter = o => Matches((SoundItem)o);

        AddButton.ToolTip = $"Add a {AudioFiles.Description} file, or a sound from a website (you can also drop files here)";
        AddButton.Click += (_, _) => SoundEditorDialog.Add(_sb);
        StopAllButton.Click += (_, _) => _sb.StopAll();
        RandomButton.Click += (_, _) => _sb.PlayRandom(_view.Cast<SoundItem>().ToList());
        Pads.SelectionChanged += (_, e) =>
        {
            foreach (SoundItem r in e.RemovedItems) _selectionOrder.Remove(r);
            foreach (SoundItem a in e.AddedItems) if (!_selectionOrder.Contains(a)) _selectionOrder.Add(a);
        };

        Pads.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (ItemAt(e.OriginalSource) is not { } item) return;
            // Ctrl/Shift+click selects several pads (e.g. to make a sequence) instead of playing.
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
            Pads.SelectedItem = item;
            Pads.Focus();
            if (item.IsMissing) { Locate(item); e.Handled = true; return; }
            _pressed = item;
            _sb.Press(item.Id);
        };
        Pads.PreviewMouseLeftButtonUp += (_, _) => ReleasePressed();
        Pads.MouseLeave += (_, _) => { if (Mouse.LeftButton != MouseButtonState.Pressed) ReleasePressed(); };
        Pads.LostMouseCapture += (_, _) => ReleasePressed();
        Pads.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) || Pads.SelectedItem is not SoundItem item) return;
            e.Handled = true;
            if (e.IsRepeat) return;
            if (item.IsMissing) { Locate(item); return; }
            _pressed = item;
            _sb.Press(item.Id);
        };
        Pads.PreviewKeyUp += (_, e) => { if (e.Key is Key.Enter or Key.Space) ReleasePressed(); };
        Pads.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (ItemAt(e.OriginalSource) is not { } item) return;
            if (!Pads.SelectedItems.Contains(item)) Pads.SelectedItem = item;
            OpenMenu(item);
            e.Handled = true;
        };
        Pads.MouseDoubleClick += (_, e) => e.Handled = true; // a double click is two presses, not "edit"

        DragEnter += OnDrag;
        DragOver += OnDrag;
        DragLeave += (_, _) => DropHint.Visibility = Visibility.Collapsed;
        Drop += async (_, e) =>
        {
            DropHint.Visibility = Visibility.Collapsed;
            if (DroppedFiles(e) is { Length: > 0 } files) await _sb.ImportFilesAsync(files, CurrentCategory());
        };

        _sb.LibraryChanged += Rebuild; // pages are cached for the app's lifetime, like the controller
        Rebuild();
    }

    public void SetQuery(string query)
    {
        _query = query.Trim();
        Heading.Text = _query.Length == 0 ? "Quick Access" : $"Results for “{_query}”";
        Refresh();
    }

    private void ReleasePressed()
    {
        if (_pressed is null) return;
        _sb.Release(_pressed.Id);
        _pressed = null;
    }

    private SoundItem? ItemAt(object source) =>
        source is DependencyObject d && ItemsControl.ContainerFromElement(Pads, d) is ListBoxItem { DataContext: SoundItem item } ? item : null;

    private string? CurrentCategory() => _filter is All or Favourites or Recent or Missing ? null : _filter;

    private bool Matches(SoundItem p)
    {
        var ok = _filter switch
        {
            All => true,
            Favourites => p.Favorite,
            Recent => _sb.Config.Recent.Contains(p.Id),
            Missing => p.HasProblem,
            _ => string.Equals(p.Category, _filter, StringComparison.OrdinalIgnoreCase),
        };
        return ok && (_query.Length == 0
            || p.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || p.Category.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || p.Shortcut.Equals(_query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Rebuilds the filter chips (categories in use, Missing when needed) and refreshes the grid.</summary>
    private void Rebuild()
    {
        var used = _sb.Items.Select(i => i.Category).Distinct(StringComparer.OrdinalIgnoreCase);
        var cats = _sb.Library.Categories().Where(c => used.Contains(c, StringComparer.OrdinalIgnoreCase));
        var chips = new List<string> { All, Favourites, Recent };
        chips.AddRange(cats);
        if (_sb.Items.Any(i => i.HasProblem)) chips.Add(Missing);
        if (!chips.Contains(_filter)) _filter = All;

        Filters.Children.Clear();
        foreach (var f in chips)
        {
            var chip = new RadioButton
            {
                Content = f == Missing ? $"{f} ({_sb.Items.Count(i => i.HasProblem)})" : f,
                GroupName = "sbfilter",
                Style = (Style)FindResource("Chip"),
                IsChecked = f == _filter,
                Margin = new Thickness(0, 0, 8, 8),
            };
            if (f == Missing) chip.Foreground = (System.Windows.Media.Brush)FindResource("BadBrush");
            chip.Checked += (_, _) => { _filter = f; Refresh(); };
            Filters.Children.Add(chip);
        }
        Refresh();
    }

    private void Refresh()
    {
        _view.CustomSort = _filter == Recent ? new RecentOrder(_sb.Config.Recent) : null;
        _view.Refresh();
        var any = !_view.IsEmpty;
        Empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        if (any) return;
        if (_sb.Items.Count == 0)
        {
            EmptyIcon.Text = Glyph.Add;
            EmptyTitle.Text = "No sounds yet";
            EmptyText.Text = $"Click Add Sound, or drag {AudioFiles.Description} files onto this page.\nYou can also paste a Myinstants link.";
        }
        else
        {
            EmptyIcon.Text = Glyph.Search;
            EmptyTitle.Text = _query.Length > 0 ? "No matches" : "Nothing here yet";
            EmptyText.Text = _query.Length > 0 ? "No sounds match your search." : _filter switch
            {
                Favourites => "Right-click a sound and choose Add to favourites.",
                Recent => "Sounds you play show up here.",
                _ => "No sounds in this view.",
            };
        }
    }

    private sealed class RecentOrder(List<string> recent) : IComparer
    {
        public int Compare(object? x, object? y) =>
            recent.IndexOf(((SoundItem)x!).Id).CompareTo(recent.IndexOf(((SoundItem)y!).Id));
    }

    private void OpenMenu(SoundItem item)
    {
        var menu = new ContextMenu { PlacementTarget = Pads };
        MenuItem Item(string text, string glyph, Action click, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            Ui.SetIcon(mi, glyph);
            mi.Click += (_, _) => click();
            menu.Items.Add(mi);
            return mi;
        }

        if (item.IsMissing) Item("Locate file…", "", () => Locate(item));
        else Item(item.Playing ? "Stop" : "Play", item.Playing ? Glyph.Stop : Glyph.Play,
            () => { if (item.Playing) _sb.Stop(item.Id); else { _sb.Press(item.Id); _sb.Release(item.Id); } },
            enabled: item.State == SoundState.Ready);
        Item("Edit…", "", () => SoundEditorDialog.Edit(_sb, item.Id));
        var chosen = _selectionOrder.Where(i => Pads.SelectedItems.Contains(i) && !i.IsSequence).ToList();
        if (chosen.Count >= 2)
            Item($"Make a sequence of {chosen.Count} sounds", "", () => _ = _sb.AddSequenceAsync([.. chosen.Select(i => i.Id)]));
        else
            Item("Make a sequence…", "", () => _sb.RaiseNotice("Ctrl+click two or more pads in the order they should play, then right-click one of them.", false));
        Item(item.Favorite ? "Remove from favourites" : "Add to favourites", item.Favorite ? Glyph.Star : Glyph.StarFill, () => _sb.ToggleFavorite(item.Id));
        if (_sb.Library.Find(item.Id) is { } entry && File.Exists(entry.FilePath))
            Item("Show file in folder", "", () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{entry.FilePath}\""));
        menu.Items.Add(new Separator());
        var remove = Item("Remove", "", () =>
        {
            if (DialogWindow.Confirm("Remove sound", $"Remove “{item.Name}” from your soundboard? Its copy in the sound library is deleted too.", "Remove", danger: true))
                _sb.Remove(item.Id);
        });
        remove.Foreground = (System.Windows.Media.Brush)FindResource("BadBrush");
        menu.IsOpen = true;
    }

    private void Locate(SoundItem item)
    {
        var dlg = new OpenFileDialog { Filter = AudioFiles.DialogFilter, Title = $"Locate “{item.Name}”" };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) _ = _sb.RelocateAsync(item.Id, dlg.FileName);
    }

    private void OnDrag(object sender, DragEventArgs e)
    {
        var ok = DroppedFiles(e) is { Length: > 0 } files && files.Any(AudioFiles.IsSupported);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private static string[]? DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
}
