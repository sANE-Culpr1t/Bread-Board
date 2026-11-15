using BreadBoard.Audio.Soundboard;
using BreadBoard.Core.Models;

namespace BreadBoard.App.Services;

/// <summary>Sequences and random sound.</summary>
public sealed partial class SoundboardController
{
    private readonly Random _random = new();

    public void RaiseNotice(string text, bool error) => Notice?.Invoke(text, error);

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
        var engine = _audio.Value.Soundboard;
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

    /// <summary>Your favourites, or every sound when you have none (random play and the mini-player).</summary>
    public IReadOnlyList<SoundItem> FavouritesOrAll()
    {
        var fav = Items.Where(i => i.Favorite).ToList();
        return fav.Count > 0 ? fav : [.. Items];
    }

    /// <summary>Plays a random playable sound from <paramref name="pool"/> (default: <see cref="FavouritesOrAll"/>).</summary>
    public SoundItem? PlayRandom(IEnumerable<SoundItem>? pool = null)
    {
        var candidates = (pool ?? FavouritesOrAll())
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
}
