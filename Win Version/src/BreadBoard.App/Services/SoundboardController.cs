using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.ViewModels;
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Import;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.App.Services;

/// <summary>Values the sound editor saves in one go.</summary>
public sealed record SoundEdit(string Name, string Category, string? Hotkey, PlayMode Mode, int Volume, bool Favorite, string Icon, string Tint);

/// <summary>Registration result for one shortcut owner ("sound:&lt;id&gt;", "global:stopAll", …).</summary>
public sealed record HotkeyState(string OwnerKey, string Label, string? Combo, string? Error, bool Pending = false);

/// <summary>
/// Connects the sound library (config + files), the audio engine and global hotkeys, and exposes
/// UI-thread state for the pages. All public members must be called on the UI thread.
/// The audio engine starts only once there is something to play.
/// </summary>
public sealed class SoundboardController : IDisposable
{
    private readonly AppHost _host;
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, SoundItem> _byId = [];
    private readonly Dictionary<string, HotkeyState> _hotkeys = [];
    private readonly DispatcherTimer _tick;
    private Task? _engineStart;
    private int _hotkeySuspend;
    private HttpClient? _http;

    public SoundboardController(AppHost host, Dispatcher ui)
    {
        _host = host;
        _ui = ui;
        Library = new SoundLibrary(host.Config, AppPaths.SoundLibrary);
        foreach (var s in host.Config.Current.Sounds) AddItem(s);
        _tick = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = TimeSpan.FromMilliseconds(33) };
        _tick.Tick += (_, _) => OnTick();
    }

    public SoundLibrary Library { get; }
    public AppConfig Config => _host.Config.Current;
    public ObservableCollection<SoundItem> Items { get; } = [];
    public IReadOnlyCollection<HotkeyState> Hotkeys => _hotkeys.Values;
    public bool EngineStarted => _engineStart is { IsCompletedSuccessfully: true };
    public string EngineSummary { get; private set; } = "Starts when you add a sound";
    public bool EngineOk { get; private set; }
    public float SoundboardLevel { get; private set; }

    /// <summary>Short message for the user (text, isError).</summary>
    public event Action<string, bool>? Notice;
    public event Action? LibraryChanged;
    public event Action? HotkeysChanged;
    public event Action? EngineChanged;
    /// <summary>Raised every frame while sounds play (progress / level) and once when playback stops.</summary>
    public event Action? PlaybackTick;
    public Action? ToggleMiniRequested { get; set; }

    public SoundItem? Get(string id) => _byId.GetValueOrDefault(id);

    // ---- startup ------------------------------------------------------------------------

    /// <summary>Called after the first frame: shortcuts first (cheap), then audio if there are sounds.</summary>
    public async Task InitializeAsync()
    {
        await RegisterHotkeysAsync();
        if (Items.Count > 0) await EnsureEngineAsync();
    }

    private Task EnsureEngineAsync() => _engineStart ??= StartEngineAsync();

    private async Task StartEngineAsync()
    {
        var audio = _host.Audio;
        audio.Soundboard.PlayingChanged += ids => _ui.BeginInvoke(() => OnPlayingChanged(ids));
        audio.StatusChanged += _ => _ui.BeginInvoke(UpdateEngineSummary);
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
        foreach (var item in Items.ToList()) await LoadAsync(item);
    }

    /// <summary>
    /// The engine gets a copy of the settings limited to what this iteration controls:
    /// soundboard → virtual cable (+ this PC's speakers). Mic and app capture join in Iterations 3–4.
    /// </summary>
    private AppConfig AudioConfig()
    {
        var c = ConfigStore.Clone(_host.Config.Current);
        c.Devices.MicEnabled = false;
        c.Devices.AppSource = null;
        c.Devices.VirtualMicEnabled = true;
        c.Devices.MonitorEnabled = c.Playback.HearOnThisPc;
        c.Devices.MonitorDeviceId = null;
        c.Channels.Soundboard.Muted = false;
        c.Channels.Soundboard.ToVirtualMic = true;
        c.Channels.Soundboard.ToMonitor = true;
        c.Channels.Master.Muted = false;
        return c;
    }

    /// <summary>Re-applies playback settings; <paramref name="devices"/> also reopens outputs (hear-on-this-PC).</summary>
    public async Task ApplyPlaybackSettingsAsync(bool devices = false)
    {
        if (_engineStart is null) return; // picked up when the engine starts
        await _engineStart;
        if (devices) await _host.Audio.StartAsync(AudioConfig());
        else _host.Audio.ApplyMixSettings(AudioConfig());
        UpdateEngineSummary();
    }

    private void UpdateEngineSummary()
    {
        if (!_host.AudioCreated) return;
        var s = _host.Audio.Status;
        if (s.Output.State == SourceState.Running)
        {
            EngineOk = true;
            EngineSummary = s.Monitor.State == SourceState.Running
                ? $"To {Name(s.Output.Name)} + {Name(s.Monitor.Name)}"
                : $"To {Name(s.Output.Name)}";
        }
        else
        {
            EngineOk = false;
            EngineSummary = s.Output.Error ?? "Virtual cable not available";
        }
        EngineChanged?.Invoke();

        static string Name(string? name) => name is null ? "?" : DeviceService.IsVirtualCable(name) ? "virtual cable" : name;
    }

    private async Task LoadAsync(SoundItem item)
    {
        if (Library.Find(item.Id) is not { } entry) return;
        if (SoundLibrary.FileMissing(entry))
        {
            item.State = SoundState.Missing;
            Log.Warn($"Sound file missing: {entry.FilePath}");
            return;
        }
        item.State = SoundState.Loading;
        try
        {
            var clip = await _host.Audio.LoadSoundAsync(entry);
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

    // ---- playback -----------------------------------------------------------------------

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
        if (!EngineOk) Notice?.Invoke(EngineSummary, true);
        _host.Audio.Soundboard.Trigger(id, keyDown: true);
        Library.MarkPlayed(id);
    }

    public void Release(string id)
    {
        if (EngineStarted && Get(id) is { Mode: PlayMode.Hold }) _host.Audio.Soundboard.Trigger(id, keyDown: false);
    }

    public void Stop(string id)
    {
        if (EngineStarted) _host.Audio.Soundboard.Stop(id);
    }

    public void StopAll()
    {
        if (EngineStarted) _host.Audio.Soundboard.StopAll();
    }

    public bool AnyPlaying => _byId.Values.Any(i => i.Playing);

    private void OnPlayingChanged(IReadOnlyList<string> ids)
    {
        foreach (var item in _byId.Values) item.Playing = ids.Contains(item.Id);
        if (ids.Count > 0) { if (!_tick.IsEnabled) _tick.Start(); }
        else
        {
            _tick.Stop();
            foreach (var item in _byId.Values) item.Progress = 0;
            SoundboardLevel = 0;
            PlaybackTick?.Invoke();
        }
    }

    private void OnTick()
    {
        var engine = _host.Audio.Soundboard;
        foreach (var item in _byId.Values)
            if (item.Playing) item.Progress = engine.GetProgress(item.Id) % 1.0;
        SoundboardLevel = _host.Audio.Mixer.Board.TakePeak();
        PlaybackTick?.Invoke();
    }

    // ---- library ------------------------------------------------------------------------

    /// <summary>Imports files (drag-and-drop or picker). Returns the sounds that were added.</summary>
    public async Task<IReadOnlyList<SoundItem>> ImportFilesAsync(IEnumerable<string> paths, string? category = null)
    {
        var added = new List<SoundItem>();
        var skipped = new List<string>();
        foreach (var path in paths)
        {
            if (!SoundLibrary.IsSupported(path)) { skipped.Add(Path.GetFileName(path)); continue; }
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
        if (skipped.Count > 0) Notice?.Invoke($"Skipped {string.Join(", ", skipped)} — only WAV, MP3 and OGG are supported.", true);
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
        });
        var entry = Library.Find(id)!;
        _byId[id].Apply(entry);
        if (EngineStarted) _host.Audio.Soundboard.Update(id, entry.Mode, entry.Volume);
        LibraryChanged?.Invoke();
        await RegisterHotkeysAsync();
        if (_hotkeys.GetValueOrDefault($"sound:{id}") is { Error: { } err, Combo: { } combo })
            Notice?.Invoke($"{combo} couldn't be registered: {err}", true);
    }

    public void ToggleFavorite(string id)
    {
        Library.ToggleFavorite(id);
        if (Library.Find(id) is { } e) _byId[id].Apply(e);
        LibraryChanged?.Invoke();
    }

    public void Remove(string id)
    {
        if (EngineStarted) _host.Audio.Soundboard.Unregister(id);
        var hadHotkey = Library.Find(id)?.Hotkey is not null;
        Library.Remove(id);
        if (_byId.Remove(id, out var item)) Items.Remove(item);
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

    // ---- hotkeys ------------------------------------------------------------------------

    /// <summary>Stops global shortcuts while the user records a new one (so the keys reach the dialog).</summary>
    public IDisposable SuspendHotkeys()
    {
        if (_hotkeySuspend++ == 0) _host.Hotkeys.UnregisterAll();
        return new Resume(this);
    }

    private sealed class Resume(SoundboardController c) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--c._hotkeySuspend == 0) _ = c.RegisterHotkeysAsync();
        }
    }

    /// <summary>(Re)registers every shortcut and records which ones Windows accepted.</summary>
    public async Task RegisterHotkeysAsync()
    {
        if (_hotkeySuspend > 0) return;
        await _hotkeyLock.WaitAsync(); // runs one at a time; the last call's result wins
        try { await RegisterHotkeysCoreAsync(); }
        finally { _hotkeyLock.Release(); }
        if (_hotkeySuspend > 0) _host.Hotkeys.UnregisterAll(); // suspended while we were registering
    }

    private readonly SemaphoreSlim _hotkeyLock = new(1, 1);

    private async Task RegisterHotkeysCoreAsync()
    {
        var svc = _host.Hotkeys;
        svc.UnregisterAll();
        _hotkeys.Clear();
        var cfg = _host.Config.Current;
        var seen = new Dictionary<string, string>(); // combo → label of first owner

        async Task Add(string owner, string label, string? combo, Action down, Action? up = null)
        {
            if (!Hotkey.TryParse(combo, out var hk)) { _hotkeys[owner] = new(owner, label, null, null); return; }
            var text = hk.ToString();
            string? error;
            if (seen.TryGetValue(text, out var first)) error = $"also assigned to “{first}”";
            else
            {
                seen[text] = label;
                error = await svc.RegisterAsync(hk, down, up);
            }
            if (error is not null) Log.Warn($"Hotkey {text} for {label} not registered: {error}");
            _hotkeys[owner] = new(owner, label, text, error);
        }

        foreach (var s in cfg.Sounds.ToList())
        {
            var id = s.Id;
            await Add($"sound:{id}", s.Name, s.Hotkey,
                () => _ui.BeginInvoke(() => Press(id)),
                s.Mode == PlayMode.Hold ? () => _ui.BeginInvoke(() => Release(id)) : null);
        }
        await Add("global:stopAll", "Stop all sounds", cfg.Hotkeys.StopAll, () => _ui.BeginInvoke(StopAll));
        await Add("global:toggleMini", "Toggle mini-player", cfg.Hotkeys.ToggleMini, () => _ui.BeginInvoke(() => ToggleMiniRequested?.Invoke()));
        // Mic mute and push-to-talk control the microphone, which joins the mix in Iteration 4.
        _hotkeys["global:muteMic"] = new("global:muteMic", "Mute microphone", cfg.Hotkeys.MuteMic, null, Pending: true);
        _hotkeys["global:ptt"] = new("global:ptt", "Push-to-talk", cfg.Processing.PushToTalk.Key, null, Pending: true);

        foreach (var item in _byId.Values) item.HotkeyError = _hotkeys.GetValueOrDefault($"sound:{item.Id}")?.Error;
        HotkeysChanged?.Invoke();
    }

    /// <summary>Changes only a sound's shortcut.</summary>
    public Task SetSoundHotkeyAsync(string id, string? combo)
    {
        var e = Library.Find(id) ?? throw new KeyNotFoundException(id);
        return SaveAsync(id, new SoundEdit(e.Name, e.Category, combo, e.Mode, e.Volume, e.Favorite, e.Icon, e.Tint));
    }

    /// <summary>Changes a global shortcut (Stop all / Toggle mini-player).</summary>
    public async Task SetGlobalHotkeyAsync(string ownerKey, string? combo)
    {
        _host.Config.Update(c =>
        {
            switch (ownerKey)
            {
                case "global:stopAll": c.Hotkeys.StopAll = combo; break;
                case "global:toggleMini": c.Hotkeys.ToggleMini = combo; break;
                default: throw new ArgumentOutOfRangeException(nameof(ownerKey));
            }
        });
        await RegisterHotkeysAsync();
        if (_hotkeys.GetValueOrDefault(ownerKey) is { Error: { } err, Combo: { } c2 })
            Notice?.Invoke($"{c2} couldn't be registered: {err}", true);
    }

    public void Dispose()
    {
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
