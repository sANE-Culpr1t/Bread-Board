using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BreadBoard.App.Services;
using BreadBoard.App.Theme;
using BreadBoard.App.ViewModels;
using BreadBoard.Core.Config;
using BreadBoard.Core.Models;

namespace BreadBoard.App.Views;

/// <summary>
/// Floating Gaming Mode window: draggable by its header, resizable from any edge, optional
/// always-on-top. Size, position and pin state are remembered. Previous/Next step through your
/// favourites (or every sound when there are none); Play plays the selected one.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private readonly ConfigService _config;
    private readonly SoundboardController _sb;
    private double _expandedHeight;
    private string? _selectedId;

    public MiniPlayerWindow(ConfigService config, SoundboardController sb)
    {
        _config = config;
        _sb = sb;
        InitializeComponent();
        var ui = config.Current.Ui;
        Width = Math.Max(MinWidth, ui.MiniWidth);
        Height = Math.Max(MinHeight, ui.MiniHeight);
        if (ui.MiniLeft is { } l && ui.MiniTop is { } t && MainWindow.IsOnScreen(l, t))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 96;
        }
        SetTopmost(ui.MiniTopmost);

        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        PinButton.Click += (_, _) =>
        {
            var on = PinButton.IsChecked == true;
            Topmost = on;
            _config.Update(c => c.Ui.MiniTopmost = on);
        };
        CollapseButton.Click += (_, _) => ToggleCollapsed();
        CloseButton.Click += (_, _) => Close();
        SourceInitialized += (_, _) => WindowFrame.Apply(this);

        PrevButton.Click += (_, _) => Step(-1);
        NextButton.Click += (_, _) => Step(+1);
        PlayButton.PreviewMouseLeftButtonDown += (_, e) => { PlaySelected(); e.Handled = true; };
        PlayButton.PreviewMouseLeftButtonUp += (_, _) => { if (Selected is { } s) _sb.Release(s.Id); };
        PlayButton.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space && !e.IsRepeat) { PlaySelected(); e.Handled = true; } };
        StopAllButton.Click += (_, _) => _sb.StopAll();
        MicButton.Click += (_, _) => _sb.ToggleMicMute();
        MasterButton.Click += (_, _) => _sb.ToggleMasterMute();
        _sb.MixChanged += Refresh;
        _sb.LibraryChanged += Refresh;
        _sb.PlaybackTick += Refresh;
        _sb.EngineChanged += Refresh;
        Closed += (_, _) =>
        {
            _sb.LibraryChanged -= Refresh;
            _sb.PlaybackTick -= Refresh;
            _sb.EngineChanged -= Refresh;
            _sb.MixChanged -= Refresh;
        };
        Refresh();
    }

    /// <summary>Favourites when there are any, otherwise every sound — in soundboard order.</summary>
    private List<SoundItem> Playlist()
    {
        var fav = _sb.Items.Where(i => i.Favorite).ToList();
        return fav.Count > 0 ? fav : [.. _sb.Items];
    }

    private SoundItem? Selected
    {
        get
        {
            var list = Playlist();
            return list.Find(i => i.Id == _selectedId) ?? list.FirstOrDefault();
        }
    }

    private void Step(int delta)
    {
        var list = Playlist();
        if (list.Count == 0) return;
        var i = Math.Max(0, list.FindIndex(x => x.Id == Selected?.Id));
        _selectedId = list[(i + delta + list.Count) % list.Count].Id;
        Refresh();
    }

    private void PlaySelected()
    {
        if (Selected is not { } s) return;
        if (s.Playing && s.Mode != PlayMode.OneShot) _sb.Stop(s.Id);
        else _sb.Press(s.Id);
    }

    private void Refresh()
    {
        var list = Playlist();
        var s = Selected;
        PrevButton.IsEnabled = NextButton.IsEnabled = list.Count > 1;
        PlayButton.IsEnabled = s is not null;
        if (s is null)
        {
            NowTitle.Text = "No sounds yet";
            NowDetail.Text = "Add sounds in the main window";
            NowProgress.Value = 0;
        }
        else
        {
            var pos = list.IndexOf(s) + 1;
            NowTitle.Text = s.Name;
            var where = list.Count == _sb.Items.Count && !s.Favorite ? "Sound" : "Favourite";
            NowDetail.Text = s.HasProblem ? s.ProblemText : $"{where} {pos} of {list.Count}{(s.HasShortcut ? $" · {s.Shortcut}" : "")}";
            NowProgress.Value = s.Playing ? s.Progress : 0;
        }
        var stop = s is { Playing: true } && s.Mode != PlayMode.OneShot;
        PlayButton.Content = stop ? Glyph.Stop : Glyph.Play;
        PlayButton.ToolTip = stop ? "Stop" : s is null ? "Play" : $"Play {s.Name}";
        EngineDot.Fill = (Brush)FindResource(_sb.EngineOk ? "OkBrush" : "MuteBrush");
        var micMuted = _sb.Config.Channels.Mic.Muted;
        MicButton.Content = micMuted ? "Mic muted" : "Mic";
        MicButton.Foreground = (Brush)FindResource(micMuted ? "BadBrush" : "InkBrush");
        var masterMuted = _sb.Config.Channels.Master.Muted;
        MasterButton.Content = masterMuted ? "Muted" : "Master";
        MasterButton.Foreground = (Brush)FindResource(masterMuted ? "BadBrush" : "InkBrush");
        EngineDot.ToolTip = _sb.EngineSummary;
    }

    public bool IsCollapsed => Body.Visibility != Visibility.Visible;

    public void SetTopmost(bool on)
    {
        Topmost = on;
        PinButton.IsChecked = on;
        PinButton.ToolTip = on ? "Kept on top — click to release" : "Keep on top";
    }

    private void ToggleCollapsed()
    {
        if (!IsCollapsed)
        {
            _expandedHeight = Height;
            Body.Visibility = Visibility.Collapsed;
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
            CollapseButton.Content = "";
            CollapseButton.ToolTip = "Expand";
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            Body.Visibility = Visibility.Visible;
            MinHeight = 300;
            Height = Math.Max(300, _expandedHeight);
            CollapseButton.Content = "";
            CollapseButton.ToolTip = "Collapse";
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        var h = IsCollapsed ? _expandedHeight : Height;
        _config.Update(c =>
        {
            c.Ui.MiniLeft = Left;
            c.Ui.MiniTop = Top;
            c.Ui.MiniWidth = Width;
            c.Ui.MiniHeight = h;
        });
    }
}
