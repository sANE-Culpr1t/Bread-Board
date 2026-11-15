using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.App.Services;

/// <summary>Global shortcuts: registration, conflicts, suspend while recording, push-to-talk wiring.</summary>
public sealed partial class SoundboardController
{
    private readonly SemaphoreSlim _hotkeyLock = new(1, 1);

    /// <summary>
    /// The shortcut state of a sound ("sound:&lt;id&gt;") or global action: what Windows said when it was
    /// registered, or the saved combination while registration hasn't run yet.
    /// </summary>
    public HotkeyState HotkeyFor(string ownerKey)
    {
        if (_hotkeys.TryGetValue(ownerKey, out var state)) return state;
        if (HotkeyRules.TrySoundId(ownerKey, out var id))
            return new(ownerKey, Library.Find(id)?.Name ?? "", Library.Find(id)?.Hotkey, null);
        var g = HotkeyRules.Global(ownerKey);
        return new(ownerKey, g.Label, g.Get(Config), null);
    }

    /// <summary>Stops global shortcuts while the user records a new one (so the keys reach the dialog).</summary>
    public IDisposable SuspendHotkeys()
    {
        if (_hotkeySuspend++ == 0) _hotkeyService.Value.UnregisterAll();
        return new Resume(this);
    }

    private sealed class Resume(SoundboardController c) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--c._hotkeySuspend == 0) _ = c.ResumeHotkeysAsync();
        }
    }

    /// <summary>Shortcuts changed while suspended (e.g. in the sound editor): their errors are reported once registration resumes.</summary>
    private readonly HashSet<string> _reportAfterResume = [];

    private async Task ResumeHotkeysAsync()
    {
        await RegisterHotkeysAsync();
        if (_hotkeySuspend > 0) return;
        foreach (var owner in _reportAfterResume) ReportHotkeyError(owner);
        _reportAfterResume.Clear();
    }

    /// <summary>(Re)registers every shortcut and records which ones Windows accepted.</summary>
    public async Task RegisterHotkeysAsync()
    {
        if (_hotkeySuspend > 0) return;
        await _hotkeyLock.WaitAsync(); // one at a time; the last call's result wins
        try { await RegisterHotkeysCoreAsync(); }
        finally { _hotkeyLock.Release(); }
        if (_hotkeySuspend > 0) _hotkeyService.Value.UnregisterAll(); // suspended while we were registering
    }

    private async Task RegisterHotkeysCoreAsync()
    {
        var svc = _hotkeyService.Value;
        svc.UnregisterAll();
        _hotkeys.Clear();
        var cfg = _config.Current;
        var seen = new Dictionary<string, string>(); // combo -> label of first owner

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
            await Add(HotkeyRules.SoundOwner(id), s.Name, s.Hotkey,
                () => _ui.BeginInvoke(() => Press(id)),
                s.Mode == PlayMode.Hold ? () => _ui.BeginInvoke(() => Release(id)) : null);
        }
        foreach (var g in HotkeyRules.Globals)
            if (GlobalAction(g.Key) is { } action)
                await Add(g.Key, g.Label, g.Get(cfg), () => _ui.BeginInvoke(action));
        ApplyPushToTalkWatch(cfg);

        foreach (var item in _byId.Values) item.HotkeyError = _hotkeys.GetValueOrDefault(HotkeyRules.SoundOwner(item.Id))?.Error;
        HotkeysChanged?.Invoke();
    }

    /// <summary>What a global shortcut does when pressed; push-to-talk is watched instead (see <see cref="ApplyPushToTalkWatch"/>).</summary>
    private Action? GlobalAction(string key) => key switch
    {
        HotkeyRules.StopAll => StopAll,
        HotkeyRules.ToggleMini => () => ToggleMiniRequested?.Invoke(),
        HotkeyRules.RandomSound => () => PlayRandom(),
        HotkeyRules.MuteMic => ToggleMicMute,
        HotkeyRules.PushToTalk => null,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key),
    };

    /// <summary>Changes the shortcut of a sound or a global action and reports a registration failure.</summary>
    public async Task SetHotkeyAsync(string ownerKey, string? combo)
    {
        if (HotkeyRules.TrySoundId(ownerKey, out var id))
        {
            var e = Library.Find(id) ?? throw new KeyNotFoundException(id);
            await SaveAsync(id, SoundEdit.From(e) with { Hotkey = combo });
            return;
        }
        _config.Update(c => HotkeyRules.Global(ownerKey).Set(c, combo));
        await RegisterHotkeysAsync();
        ReportHotkeyError(ownerKey);
    }

    private void ReportHotkeyError(string ownerKey)
    {
        if (_hotkeySuspend > 0) { _reportAfterResume.Add(ownerKey); return; }
        if (_hotkeys.GetValueOrDefault(ownerKey) is { Error: { } err, Combo: { } combo })
            Notice?.Invoke($"{combo} couldn't be registered: {err}", true);
    }
}
