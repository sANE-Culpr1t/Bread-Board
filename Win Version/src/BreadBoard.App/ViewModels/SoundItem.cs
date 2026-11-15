using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using BreadBoard.Core.Models;

namespace BreadBoard.App.ViewModels;

public enum SoundState { Loading, Ready, Missing, Failed }

/// <summary>One pad on the soundboard: the saved entry plus live state (loading, playing, hotkey status).</summary>
public sealed class SoundItem(string id) : INotifyPropertyChanged
{
    private string _name = "", _category = "", _shortcut = "", _icon = "volume", _tint = "beige";
    private PlayMode _mode;
    private SoundRoute _route;
    private int _sequenceCount;
    private int _volume;
    private bool _favorite, _playing;
    private double _progress;
    private TimeSpan? _duration;
    private SoundState _state = SoundState.Loading;
    private string? _error, _hotkeyError;

    public string Id { get; } = id;
    public string Name { get => _name; private set => Set(ref _name, value); }
    public string Category { get => _category; private set => Set(ref _category, value); }
    public string Shortcut { get => _shortcut; private set => Set(ref _shortcut, value, [nameof(HasShortcut)]); }
    public bool HasShortcut => _shortcut.Length > 0;
    public PlayMode Mode { get => _mode; private set => Set(ref _mode, value, [nameof(ModeText), nameof(ModeGlyph), nameof(StatusText)]); }
    public int Volume { get => _volume; private set => Set(ref _volume, value); }
    public SoundRoute Route { get => _route; private set => Set(ref _route, value, [nameof(StatusText)]); }
    /// <summary>Number of sounds in a sequence; 0 for a normal sound.</summary>
    public int SequenceCount { get => _sequenceCount; private set => Set(ref _sequenceCount, value, [nameof(IsSequence), nameof(StatusText)]); }
    public bool IsSequence => _sequenceCount > 0;
    public bool Favorite { get => _favorite; private set => Set(ref _favorite, value); }
    public string IconKey { get => _icon; private set => Set(ref _icon, value, [nameof(Glyph)]); }
    public string TintKey { get => _tint; private set => Set(ref _tint, value, [nameof(TintBrush)]); }
    public string Glyph => SoundLook.Glyph(_icon);
    public Brush TintBrush => SoundLook.Brush(_tint);

    public bool Playing { get => _playing; set => Set(ref _playing, value, [nameof(StatusText)]); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public TimeSpan? Duration { get => _duration; set => Set(ref _duration, value, [nameof(DurationText)]); }
    public SoundState State { get => _state; set => Set(ref _state, value, [nameof(StatusText), nameof(HasProblem), nameof(ProblemText), nameof(IsMissing)]); }
    public string? Error { get => _error; set => Set(ref _error, value, [nameof(ProblemText)]); }
    public string? HotkeyError { get => _hotkeyError; set => Set(ref _hotkeyError, value, [nameof(HasProblem), nameof(ProblemText)]); }

    public bool IsMissing => _state == SoundState.Missing;
    public bool HasProblem => _state is SoundState.Missing or SoundState.Failed || _hotkeyError is not null;
    public string? ProblemText => _state switch
    {
        SoundState.Missing => "File missing — right-click to locate it",
        SoundState.Failed => $"Can't play: {_error}",
        _ => _hotkeyError is null ? null : $"Shortcut not active: {_hotkeyError}",
    };

    public string DurationText => _duration is not { } d ? "" : d.TotalSeconds < 60 ? $"{d.TotalSeconds:0.0} s" : $"{(int)d.TotalMinutes}:{d.Seconds:00}";
    public string ModeText => ModeLabel(_mode);
    public string ModeGlyph => _mode switch { PlayMode.Loop => "", PlayMode.Hold => "", PlayMode.Toggle => "", _ => "" };
    public string StatusText => _state switch
    {
        SoundState.Loading => "Loading…",
        SoundState.Missing => "Missing",
        SoundState.Failed => "Error",
        _ => _playing ? "Playing" : string.Join(" · ", new[]
        {
            _sequenceCount > 0 ? $"{_sequenceCount} sounds" : null,
            _mode == PlayMode.OneShot ? null : ModeText,
            _route == SoundRoute.Both ? null : _route == SoundRoute.MicOnly ? "Mic only" : "Headphones only",
        }.Where(s => s is not null)),
    };

    public static string RouteLabel(SoundRoute r) => r switch
    {
        SoundRoute.MicOnly => "Virtual mic only (you don't hear it)",
        SoundRoute.MonitorOnly => "Headphones only (private preview)",
        _ => "Virtual mic and headphones",
    };

    public static string ModeLabel(PlayMode m) => m switch
    {
        PlayMode.OneShot => "One-shot",
        PlayMode.Toggle => "Toggle",
        PlayMode.Loop => "Loop",
        PlayMode.Hold => "Hold",
        _ => m.ToString(),
    };

    public void Apply(SoundEntry e)
    {
        Name = e.Name;
        Category = e.Category;
        Shortcut = e.Hotkey ?? "";
        Mode = e.Mode;
        Volume = e.Volume;
        Favorite = e.Favorite;
        IconKey = e.Icon;
        TintKey = e.Tint;
        Route = e.Route;
        SequenceCount = e.Sequence?.Count ?? 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, string[]? also = null, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
        if (also is not null) foreach (var a in also) Raise(a);
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Pad icons and colours a sound can use (stored by key in the config).</summary>
public static class SoundLook
{
    public static readonly (string Key, string Glyph)[] Icons =
    [
        ("volume", ""), ("emoji", ""), ("bolt", ""),
        ("music", ""), ("star", ""), ("heart", ""), ("game", ""),
        ("alert", ""), ("chat", ""),
    ];

    public static readonly (string Key, string Hex)[] Tints =
    [
        ("beige", "#E3C8A4"), ("amber", "#D9A35B"), ("cream", "#EFE8DD"), ("slate", "#9AA7B8"),
        ("clay", "#C98A72"), ("sage", "#9FB59A"), ("mauve", "#B59AB0"),
    ];

    private static readonly Dictionary<string, Brush> Brushes = Tints.ToDictionary(t => t.Key, t =>
    {
        var b = (Brush)new BrushConverter().ConvertFromString(t.Hex)!;
        b.Freeze();
        return b;
    });

    public static string Glyph(string key) => Array.Find(Icons, i => i.Key == key).Glyph ?? Icons[0].Glyph;
    public static Brush Brush(string key) => Brushes.TryGetValue(key, out var b) ? b : Brushes["beige"];
}
