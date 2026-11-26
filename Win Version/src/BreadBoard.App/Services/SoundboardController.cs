using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Import;
using BreadBoard.Audio.Mixing;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;
using BreadBoard.Platform.Hotkeys;

namespace BreadBoard.App.Services;

/// <summary>Values the sound editor saves in one go.</summary>
public sealed record SoundEdit(string Name, string Category, string? Hotkey, PlayMode Mode, int Volume, bool Favorite, string Icon, string Tint, SoundRoute Route)
{
    /// <summary>The entry's current values, to change some of them with <c>with</c>.</summary>
    public static SoundEdit From(SoundEntry e) => new(e.Name, e.Category, e.Hotkey, e.Mode, e.Volume, e.Favorite, e.Icon, e.Tint, e.Route);
}

/// <summary>Registration result for one shortcut owner ("sound:&lt;id&gt;", "global:stopAll", …).</summary>
public sealed record HotkeyState(string OwnerKey, string Label, string? Combo, string? Error);

/// <summary>Idle until the engine starts; MonitorOnly = no virtual-mic output, so the headphones play the mix.</summary>
public enum EngineState { Idle, Live, MonitorOnly, Problem }

/// <summary>
/// Connects the sound library (config + files), the audio engine and global hotkeys, and exposes
/// UI-thread state for the pages. All public members must be called on the UI thread.
/// The audio engine starts only once there is something to play.
/// </summary>
public sealed partial class SoundboardController : IDisposable
{
    private readonly ConfigService _config;
    private readonly Lazy<AudioHost> _audio;
    private readonly Lazy<GlobalHotkeyService> _hotkeyService;
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, SoundItem> _byId = [];
    private readonly Dictionary<string, HotkeyState> _hotkeys = [];
    private readonly DispatcherTimer _tick;
    private Task? _engineStart;
    private int _hotkeySuspend;
    private HttpClient? _http;

    /// <param name="audio">Created on first use: the engine starts only once there is something to play.</param>
    /// <param name="hotkeys">Created on first use (its own message thread).</param>
    public SoundboardController(ConfigService config, Lazy<AudioHost> audio, Lazy<GlobalHotkeyService> hotkeys, Dispatcher ui)
    {
        _config = config;
        _audio = audio;
        _hotkeyService = hotkeys;
        _ui = ui;
        Library = new SoundLibrary(config, AppPaths.SoundLibrary);
        foreach (var s in config.Current.Sounds) AddItem(s);
        _tick = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = TimeSpan.FromMilliseconds(33) };
        _tick.Tick += (_, _) => OnTick();
    }

    public SoundLibrary Library { get; }
    public AppConfig Config => _config.Current;
    public ObservableCollection<SoundItem> Items { get; } = [];
    public IReadOnlyCollection<HotkeyState> Hotkeys => _hotkeys.Values;
    public bool EngineStarted => _engineStart is { IsCompletedSuccessfully: true };
    public string EngineSummary { get; private set; } = "Starts when you add a sound or choose an app";
    public EngineState Engine { get; private set; }
    /// <summary>Sound reaches someone: the virtual mic, or (monitor only) your headphones.</summary>
    public bool EngineOk => Engine is EngineState.Live or EngineState.MonitorOnly;
    /// <summary>Peaks since the last frame, while meters run (see <see cref="PlaybackTick"/>).</summary>
    public MeterLevels Levels { get; private set; }

    /// <summary>Short message for the user (text, isError).</summary>
    public event Action<string, bool>? Notice;
    public event Action? LibraryChanged;
    public event Action? HotkeysChanged;
    public event Action? EngineChanged;
    /// <summary>Raised every frame while sounds play or app audio is captured (progress / levels), and once when that stops.</summary>
    public event Action? PlaybackTick;
    public Action? ToggleMiniRequested { get; set; }

    public SoundItem? Get(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Called after the first frame: shortcuts first (cheap), then audio if there are sounds.</summary>
    public async Task InitializeAsync()
    {
        _config.Update(ProfileService.EnsureDefaults);
        await RegisterHotkeysAsync();
        if (WantsEngine) await EnsureEngineAsync();
    }

    /// <summary>The engine runs once there is something to play or capture, and keeps running after that.</summary>
    private bool WantsEngine => _engineStart is not null || Items.Count > 0 || Config.Devices.AppSource is not null || Config.Devices.MicEnabled;

    private Task EnsureEngineAsync() => _engineStart ??= StartEngineAsync();

    /// <summary>Applies the current device, app and mix settings to the engine (reopens only what changed).</summary>
    private async Task RestartAudioAsync()
    {
        if (!WantsEngine) return;
        await EnsureEngineAsync();
        await _audio.Value.StartAsync(AudioConfig());
        UpdateEngineSummary();
    }

    private async Task StartEngineAsync()
    {
        var audio = _audio.Value;
        audio.Soundboard.PlayingChanged += ids => _ui.BeginInvoke(() => OnPlayingChanged(ids));
        audio.StatusChanged += _ => _ui.BeginInvoke(UpdateEngineSummary);
        audio.Devices.DevicesChanged += () => _ui.BeginInvoke(RaiseDevicesListChanged);
        try
        {
            await audio.StartAsync(AudioConfig());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Devices are retried by the engine itself; sounds still load so they're ready when it recovers.
            Log.Error("Audio engine start failed", ex);
            Notice?.Invoke($"The audio engine couldn't start: {Friendly(ex)}", true);
        }
        UpdateEngineSummary();
        // Files first, then sequences (they are built from the loaded files).
        foreach (var item in Items.Where(i => !i.IsSequence).ToList()) await LoadAsync(item);
        foreach (var item in Items.Where(i => i.IsSequence).ToList()) await LoadAsync(item);
    }

    /// <summary>A snapshot of the settings for the engine (it reads them on its own threads).</summary>
    private AppConfig AudioConfig() => ConfigStore.Clone(_config.Current);

    /// <summary>Re-applies playback settings (stacking, voice limit).</summary>
    public async Task ApplyPlaybackSettingsAsync()
    {
        if (_engineStart is null) return; // picked up when the engine starts
        await _engineStart;
        _audio.Value.ApplyMixSettings(AudioConfig());
        UpdateEngineSummary();
    }

    private void UpdateEngineSummary()
    {
        if (!_audio.IsValueCreated || _disposed) return; // late engine events during shutdown are ignored
        var s = _audio.Value.Status;
        Status = s;
        (Engine, EngineSummary) = (s.Output.State, _audio.Value.MonitorOnly) switch
        {
            (_, true) => (EngineState.MonitorOnly, $"No virtual mic output · monitor only on {Name(s.Monitor.Name)}"),
            (SourceState.Running, _) => (EngineState.Live, s.Monitor.State == SourceState.Running
                ? $"To {Name(s.Output.Name)} + {Name(s.Monitor.Name)}"
                : $"To {Name(s.Output.Name)}"),
            _ => (EngineState.Problem, s.Output.Error ?? "Virtual cable not available"),
        };
        UpdateTicking();
        EngineChanged?.Invoke();

        static string Name(string? name) => name is null ? "?" : DeviceService.IsVirtualCable(name) ? "Bread Board mic" : name;
    }

    private async Task LoadAsync(SoundItem item)
    {
        if (Library.Find(item.Id) is not { } entry) return;
        if (entry.Sequence is not null) { LoadSequence(item, entry); return; }
        if (SoundLibrary.FileMissing(entry))
        {
            item.State = SoundState.Missing;
            Log.Warn($"Sound file missing: {entry.FilePath}");
            return;
        }
        item.State = SoundState.Loading;
        try
        {
            var clip = await _audio.Value.LoadSoundAsync(entry);
            item.Duration = clip.Duration;
            item.State = SoundState.Ready;
            item.Error = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn($"Couldn't load {entry.FilePath}", ex);
            item.Error = ex is FileNotFoundException ? null : Friendly(ex);
            item.State = ex is FileNotFoundException ? SoundState.Missing : SoundState.Failed;
        }
        LibraryChanged?.Invoke();
    }

    /// <summary>Pad pressed (mouse, keyboard or hotkey). Hold-mode sounds play until <see cref="Release"/>.</summary>
    public void Press(string id)
    {
        if (Get(id) is not { } item) return;
        switch (item.State)
        {
            case SoundState.Missing:
                Notice?.Invoke($"“{item.Name}” can't play: its file is missing. Right-click the pad to locate it.", true);
                return;
            case SoundState.Failed:
                Notice?.Invoke($"“{item.Name}” can't play: {item.Error}", true);
                return;
            case SoundState.Loading:
                return;
        }
        if (!EngineStarted) return;
        if (Engine == EngineState.Problem) Notice?.Invoke(EngineSummary, true);
        _audio.Value.Soundboard.Trigger(id, keyDown: true);
        Library.MarkPlayed(id);
    }

    public void Release(string id)
    {
        if (EngineStarted && Get(id) is { Mode: PlayMode.Hold }) _audio.Value.Soundboard.Trigger(id, keyDown: false);
    }

    public void Stop(string id)
    {
        if (EngineStarted) _audio.Value.Soundboard.Stop(id);
    }

    public void StopAll()
    {
        if (EngineStarted) _audio.Value.Soundboard.StopAll();
    }

    public bool AnyPlaying => _byId.Values.Any(static i => i.Playing);

    private void OnPlayingChanged(IReadOnlyList<string> ids)
    {
        if (_disposed) return;
        foreach (var item in _byId.Values) item.Playing = ids.Contains(item.Id);
        if (ids.Count == 0)
        {
            foreach (var item in _byId.Values) item.Progress = 0;
            Levels = Levels with { Soundboard = 0 };
        }
        UpdateTicking();
    }

    /// <summary>The meter/progress timer runs only while the window is visible and something can make sound.</summary>
    private void UpdateTicking()
    {
        var needed = _metersVisible && EngineStarted
            && (AnyPlaying || Status.App.State == SourceState.Running || Status.Mic.State == SourceState.Running);
        if (needed) { if (!_tick.IsEnabled) _tick.Start(); return; }
        if (!_tick.IsEnabled) return;
        _tick.Stop();
        Levels = default;
        PlaybackTick?.Invoke();
    }

    private void OnTick()
    {
        var engine = _audio.Value.Soundboard;
        foreach (var item in _byId.Values)
            if (item.Playing) item.Progress = engine.GetProgress(item.Id) % 1.0;
        Levels = _audio.Value.Mixer.TakeLevels();
        PlaybackTick?.Invoke();
    }

    /// <summary>Imports files (drag-and-drop or picker). Returns the sounds that were added.</summary>
    public async Task<IReadOnlyList<SoundItem>> ImportFilesAsync(IEnumerable<string> paths, string? category = null)
    {
        var added = new List<SoundItem>();
        var skipped = new List<string>();
        foreach (var path in paths)
        {
            if (!AudioFiles.IsSupported(path)) { skipped.Add(Path.GetFileName(path)); continue; }
            try
            {
                var entry = await Task.Run(() => Library.Import(path, category: category));
                if (await AddAndLoadAsync(entry) is { } item) added.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Log.Warn($"Import failed: {path}", ex);
                Notice?.Invoke($"Couldn't import {Path.GetFileName(path)}: {ex.Message}", true);
            }
        }
        if (skipped.Count > 0) Notice?.Invoke($"Skipped {string.Join(", ", skipped)} — only {AudioFiles.Description} files are supported.", true);
        else if (added.Count > 1) Notice?.Invoke($"Added {added.Count} sounds.", false);
        return added;
    }

    /// <summary>Downloads a sound from a Myinstants page or a direct audio link.</summary>
    public async Task<SoundItem?> ImportLinkAsync(string link, string? name, string? category, CancellationToken ct = default)
    {
        _http ??= SoundDownloader.CreateHttpClient();
        var dl = await new SoundDownloader(_http).DownloadAsync(link, Library.LibraryDir, ct);
        var entry = Library.Add(dl.FilePath, string.IsNullOrWhiteSpace(name) ? dl.SuggestedName : name, category, dl.SourceUrl.ToString());
        return await AddAndLoadAsync(entry);
    }

    internal async Task<SoundItem?> AddAndLoadAsync(SoundEntry entry)
    {
        var item = AddItem(entry);
        LibraryChanged?.Invoke();
        await EnsureEngineAsync();
        if (item.State == SoundState.Loading) await LoadAsync(item);
        if (item.State == SoundState.Failed)
        {
            // Not a playable file after all: undo the import instead of leaving a broken pad.
            Notice?.Invoke($"“{item.Name}” isn't a playable audio file ({item.Error}).", true);
            Remove(item.Id);
            return null;
        }
        return item;
    }

    public async Task SaveAsync(string id, SoundEdit edit)
    {
        Library.Update(id, s =>
        {
            s.Name = edit.Name;
            s.Category = edit.Category;
            s.Hotkey = edit.Hotkey;
            s.Mode = edit.Mode;
            s.Volume = edit.Volume;
            s.Favorite = edit.Favorite;
            s.Icon = edit.Icon;
            s.Tint = edit.Tint;
            s.Route = edit.Route;
        });
        var entry = Library.Find(id)!;
        _byId[id].Apply(entry);
        if (EngineStarted)
        {
            _audio.Value.Soundboard.Update(id, entry.Mode, entry.Volume, entry.Route);
            RebuildSequencesUsing(id); // its volume is baked into sequences that contain it
        }
        LibraryChanged?.Invoke();
        await RegisterHotkeysAsync();
        ReportHotkeyError(HotkeyRules.SoundOwner(id));
    }

    public void ToggleFavorite(string id)
    {
        Library.ToggleFavorite(id);
        if (Library.Find(id) is { } e) _byId[id].Apply(e);
        LibraryChanged?.Invoke();
    }

    public void Remove(string id)
    {
        if (EngineStarted) _audio.Value.Soundboard.Unregister(id);
        var hadHotkey = Library.Find(id)?.Hotkey is not null;
        Library.Remove(id);
        // Removing a sound can also remove sequences that no longer have two sounds.
        foreach (var gone in _byId.Keys.Where(k => Library.Find(k) is null).ToList())
        {
            if (EngineStarted) _audio.Value.Soundboard.Unregister(gone);
            if (_byId.Remove(gone, out var item)) Items.Remove(item);
        }
        if (EngineStarted) RebuildSequencesUsing(id);
        LibraryChanged?.Invoke();
        if (hadHotkey) _ = RegisterHotkeysAsync();
    }

    /// <summary>Points a missing sound at a new file and loads it.</summary>
    public async Task RelocateAsync(string id, string path)
    {
        try
        {
            await Task.Run(() => Library.Relocate(id, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Notice?.Invoke($"Couldn't use {Path.GetFileName(path)}: {ex.Message}", true);
            return;
        }
        await EnsureEngineAsync();
        await LoadAsync(_byId[id]);
        if (_byId[id].State == SoundState.Ready) Notice?.Invoke($"“{_byId[id].Name}” is playable again.", false);
    }

    private SoundItem AddItem(SoundEntry entry)
    {
        var item = new SoundItem(entry.Id);
        item.Apply(entry);
        _byId[entry.Id] = item;
        Items.Add(item);
        return item;
    }

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _tick.Stop();
        _hotkeyLock.Dispose();
        _http?.Dispose();
    }

    private static string Friendly(Exception ex) => ex switch
    {
        NotSupportedException => ex.Message,
        UnauthorizedAccessException => "access denied",
        _ => ex.Message.Length > 80 ? ex.Message[..80] + "…" : ex.Message,
    };
}
