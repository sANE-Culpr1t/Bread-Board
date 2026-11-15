using BreadBoard.App.ViewModels;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Config;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.App.Services;

/// <summary>Sequences, random sound and profiles (Iteration 5).</summary>
public sealed partial class SoundboardController
{
    private readonly Random _random = new();

    /// <summary>The active profile changed (sound shortcuts, routes, mixer and devices may all differ).</summary>
    public event Action? ProfileChanged;

    public string ActiveProfile => Config.ActiveProfile;

    public void RaiseNotice(string text, bool error) => Notice?.Invoke(text, error);

    // ---- sequences ----------------------------------------------------------------------

    /// <summary>Creates a sequence from sounds in the given order.</summary>
    public async Task<SoundItem?> AddSequenceAsync(IReadOnlyList<string> ids)
    {
        var playable = ids.Where(id => Get(id) is { IsSequence: false }).Distinct().ToList();
        if (playable.Count < 2)
        {
            Notice?.Invoke("Select at least two sounds (not sequences) to make a sequence.", true);
            return null;
        }
        var entry = Library.AddSequence(playable);
        var item = await AddAndLoadAsync(entry);
        if (item is not null) Notice?.Invoke($"Sequence “{item.Name}” added.", false);
        return item;
    }

    private void LoadSequence(SoundItem item, SoundEntry entry)
    {
        if (!EngineStarted) return;
        var engine = _host.Audio.Soundboard;
        var parts = new List<(SoundClip, float)>();
        foreach (var id in entry.Sequence!)
        {
            if (Library.Find(id) is not { } member || engine.GetClip(id) is not { } clip)
            {
                item.State = SoundState.Missing;
                item.Error = "a sound in it is missing";
                return;
            }
            parts.Add((clip, member.Volume / 100f));
        }
        var seq = SequenceBuilder.Build(entry.Id, parts);
        engine.Register(entry.Id, seq, entry.Mode, entry.Volume, entry.Route);
        item.Duration = seq.Duration;
        item.Error = null;
        item.State = SoundState.Ready;
        LibraryChanged?.Invoke();
    }

    private void RebuildSequencesUsing(string id)
    {
        foreach (var seq in Library.SequencesUsing(id))
            if (Get(seq.Id) is { } item) LoadSequence(item, seq);
    }

    // ---- random -------------------------------------------------------------------------

    /// <summary>Plays a random playable sound from <paramref name="pool"/> (default: favourites, or every sound).</summary>
    public SoundItem? PlayRandom(IEnumerable<SoundItem>? pool = null)
    {
        var candidates = (pool ?? (Items.Any(i => i.Favorite) ? Items.Where(i => i.Favorite) : Items))
            .Where(i => i.State == SoundState.Ready && i.Mode != PlayMode.Hold).ToList();
        if (candidates.Count == 0)
        {
            Notice?.Invoke("No playable sounds to pick from.", true);
            return null;
        }
        var pick = candidates[_random.Next(candidates.Count)];
        Press(pick.Id);
        return pick;
    }

    // ---- profiles -----------------------------------------------------------------------

    /// <summary>Saves the current settings into the active profile and applies <paramref name="name"/>.</summary>
    public async Task SwitchProfileAsync(string name)
    {
        if (name == Config.ActiveProfile) return;
        _host.Config.Update(c => ProfileService.Switch(c, name));
        foreach (var e in Config.Sounds)
        {
            if (Get(e.Id) is not { } item) continue;
            item.Apply(e);
            if (EngineStarted) _host.Audio.Soundboard.Update(e.Id, e.Mode, e.Volume, e.Route);
        }
        if (_engineStart is not null || Config.Devices.MicEnabled || Config.Devices.AppSource is not null) await EnsureEngineAsync();
        if (_engineStart is not null)
        {
            await _engineStart;
            await _host.Audio.StartAsync(AudioConfig());
            UpdateEngineSummary();
        }
        await RegisterHotkeysAsync();
        LibraryChanged?.Invoke();
        MixChanged?.Invoke();
        ProfileChanged?.Invoke();
        Notice?.Invoke($"Profile: {name}", false);
        Log.Info($"Profile {name} applied");
    }
}
