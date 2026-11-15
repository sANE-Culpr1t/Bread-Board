using System.Collections.Concurrent;
using BreadBoard.Core.Models;

namespace BreadBoard.Audio.Soundboard;

/// <summary>
/// Plays decoded clips with one-shot / toggle / loop / hold behaviour.
/// UI and hotkey threads enqueue commands; the audio thread applies them at the start of each
/// <see cref="Render"/> call, so the voice list is only ever touched by the audio thread.
/// </summary>
public sealed class SoundboardEngine
{
    private sealed class Entry(SoundClip clip, PlayMode mode, float volume)
    {
        public readonly SoundClip Clip = clip;
        public volatile PlayMode Mode = mode;
        public volatile float Volume = volume;
        public volatile SoundRoute Route;
        /// <summary>Sample position of the most recently rendered voice (written by the audio thread).</summary>
        public volatile int Position;
    }

    private sealed class Voice(Entry entry, bool loop)
    {
        public readonly Entry Entry = entry;
        public readonly bool Loop = loop;
        public int Position;
    }

    private enum CmdKind { Down, Up, Stop, StopAll }
    private readonly record struct Command(CmdKind Kind, string? Id);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly List<Voice> _voices = new(32);
    private volatile string[] _playing = [];
    private volatile bool _allowStacking = true;
    private volatile int _maxVoices = 8;

    public bool AllowStacking { get => _allowStacking; set => _allowStacking = value; }
    public int MaxVoices { get => _maxVoices; set => _maxVoices = Math.Clamp(value, 1, 32); }

    /// <summary>IDs of sounds currently playing (snapshot, safe from any thread).</summary>
    public IReadOnlyList<string> Playing => _playing;
    public event Action<IReadOnlyList<string>>? PlayingChanged;

    public void Register(string id, SoundClip clip, PlayMode mode, int volumePercent, SoundRoute route = SoundRoute.Both) =>
        _entries[id] = new Entry(clip, mode, volumePercent / 100f) { Route = route };

    /// <summary>The decoded clip of a registered sound (used to build sequences).</summary>
    public SoundClip? GetClip(string id) => _entries.TryGetValue(id, out var e) ? e.Clip : null;

    public bool IsRegistered(string id) => _entries.ContainsKey(id);

    /// <summary>0–1 position of the sound's newest voice; meaningful while it is in <see cref="Playing"/>.</summary>
    public double GetProgress(string id) =>
        _entries.TryGetValue(id, out var e) && e.Clip.Samples.Length > 0 ? (double)e.Position / e.Clip.Samples.Length : 0;

    public void Update(string id, PlayMode mode, int volumePercent, SoundRoute route = SoundRoute.Both)
    {
        if (!_entries.TryGetValue(id, out var e)) return;
        e.Mode = mode;
        e.Volume = volumePercent / 100f;
        e.Route = route;
    }

    public void Unregister(string id)
    {
        Stop(id);
        _entries.TryRemove(id, out _);
    }

    public void Trigger(string id, bool keyDown = true) => _commands.Enqueue(new(keyDown ? CmdKind.Down : CmdKind.Up, id));
    public void Stop(string id) => _commands.Enqueue(new(CmdKind.Stop, id));
    public void StopAll() => _commands.Enqueue(new(CmdKind.StopAll, null));

    /// <summary>Audio thread: mixes every active voice into <paramref name="output"/> (overwrites it), ignoring routing.</summary>
    public void Render(Span<float> output) => Render(output, []);

    /// <summary>
    /// Audio thread: mixes voices into the virtual-mic bus (<paramref name="main"/>) and the headphones bus
    /// (<paramref name="monitor"/>) according to each sound's route. Both are overwritten. With an empty
    /// monitor span every voice goes to <paramref name="main"/>.
    /// </summary>
    public void Render(Span<float> main, Span<float> monitor)
    {
        var changed = ApplyCommands();
        main.Clear();
        monitor.Clear();
        var split = monitor.Length == main.Length;
        for (var v = 0; v < _voices.Count; v++) _voices[v].Entry.Position = _voices[v].Position; // oldest first, newest wins
        for (var v = _voices.Count - 1; v >= 0; v--)
        {
            var voice = _voices[v];
            var src = voice.Entry.Clip.Samples;
            var vol = voice.Entry.Volume;
            var route = voice.Entry.Route;
            var toMain = !split || route != SoundRoute.MonitorOnly;
            var toMon = split && route != SoundRoute.MicOnly;
            var i = 0;
            while (i < main.Length)
            {
                if (voice.Position >= src.Length)
                {
                    if (!voice.Loop || src.Length == 0) break;
                    voice.Position = 0;
                }
                var n = Math.Min(main.Length - i, src.Length - voice.Position);
                var s = src.AsSpan(voice.Position, n);
                if (toMain)
                {
                    var o = main.Slice(i, n);
                    for (var k = 0; k < n; k++) o[k] += s[k] * vol;
                }
                if (toMon)
                {
                    var m = monitor.Slice(i, n);
                    for (var k = 0; k < n; k++) m[k] += s[k] * vol;
                }
                voice.Position += n;
                i += n;
            }
            if (!voice.Loop && voice.Position >= src.Length) { _voices.RemoveAt(v); changed = true; }
        }
        if (changed) Publish();
    }

    private bool ApplyCommands()
    {
        var changed = false;
        while (_commands.TryDequeue(out var c))
        {
            switch (c.Kind)
            {
                case CmdKind.StopAll:
                    changed |= _voices.Count > 0;
                    _voices.Clear();
                    break;
                case CmdKind.Stop:
                    changed |= _voices.RemoveAll(v => ReferenceEquals(v.Entry, Get(c.Id))) > 0;
                    break;
                case CmdKind.Up:
                    if (Get(c.Id) is { Mode: PlayMode.Hold } held)
                        changed |= _voices.RemoveAll(v => ReferenceEquals(v.Entry, held)) > 0;
                    break;
                case CmdKind.Down:
                    changed |= Press(c.Id!);
                    break;
            }
        }
        return changed;
    }

    private bool Press(string id)
    {
        if (Get(id) is not { } e) return false;
        var playing = _voices.Exists(v => ReferenceEquals(v.Entry, e));
        switch (e.Mode)
        {
            case PlayMode.Toggle or PlayMode.Loop when playing:
                _voices.RemoveAll(v => ReferenceEquals(v.Entry, e));
                return true;
            case PlayMode.Hold when playing:
                return false;
            case PlayMode.OneShot when !_allowStacking:
                _voices.RemoveAll(v => ReferenceEquals(v.Entry, e));
                break;
        }
        while (_voices.Count >= _maxVoices) _voices.RemoveAt(0); // drop the oldest voice
        _voices.Add(new Voice(e, e.Mode is PlayMode.Loop or PlayMode.Hold));
        return true;
    }

    private Entry? Get(string? id) => id is not null && _entries.TryGetValue(id, out var e) ? e : null;

    private void Publish()
    {
        var ids = new List<string>(_voices.Count);
        foreach (var v in _voices)
        {
            var id = v.Entry.Clip.Id;
            if (!ids.Contains(id)) ids.Add(id);
        }
        var snapshot = ids.ToArray();
        _playing = snapshot;
        var handler = PlayingChanged;
        if (handler is not null) ThreadPool.UnsafeQueueUserWorkItem(_ => handler(snapshot), null); // never block audio
    }
}
