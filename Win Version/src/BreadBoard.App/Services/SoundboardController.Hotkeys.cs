using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.ViewModels;
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Import;
using BreadBoard.Audio.Sources;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.App.Services;

/// <summary>Global shortcuts: registration, conflicts, suspend while recording, push-to-talk wiring.</summary>
public sealed partial class SoundboardController
{
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
        await Add("global:muteMic", "Mute microphone", cfg.Hotkeys.MuteMic, () => _ui.BeginInvoke(ToggleMicMute));
        await Add("global:random", "Play a random sound", cfg.Hotkeys.RandomSound, () => _ui.BeginInvoke(() => PlayRandom()));
        ApplyPushToTalkWatch(cfg);

        foreach (var item in _byId.Values) item.HotkeyError = _hotkeys.GetValueOrDefault($"sound:{item.Id}")?.Error;
        HotkeysChanged?.Invoke();
    }

    /// <summary>Changes only a sound's shortcut.</summary>
    public Task SetSoundHotkeyAsync(string id, string? combo)
    {
        var e = Library.Find(id) ?? throw new KeyNotFoundException(id);
        return SaveAsync(id, new SoundEdit(e.Name, e.Category, combo, e.Mode, e.Volume, e.Favorite, e.Icon, e.Tint, e.Route));
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
                case "global:muteMic": c.Hotkeys.MuteMic = combo; break;
                case "global:random": c.Hotkeys.RandomSound = combo; break;
                case "global:ptt": c.Processing.PushToTalk.Key = combo; break;
                default: throw new ArgumentOutOfRangeException(nameof(ownerKey));
            }
        });
        await RegisterHotkeysAsync();
        if (_hotkeys.GetValueOrDefault(ownerKey) is { Error: { } err, Combo: { } c2 })
            Notice?.Invoke($"{c2} couldn't be registered: {err}", true);
    }
}
