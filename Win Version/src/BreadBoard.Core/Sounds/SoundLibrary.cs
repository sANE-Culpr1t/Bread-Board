using BreadBoard.Core.Config;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Core.Models;

namespace BreadBoard.Core.Sounds;

/// <summary>
/// The user's sound collection: entries in the config plus their audio files in the library folder.
/// Imported files are copied into the library so moving or deleting the original doesn't break a pad.
/// Pure file/config logic — decoding and playback live in the audio project.
/// </summary>
public sealed class SoundLibrary(ConfigService config, string libraryDir)
{
    public const int MaxRecent = 12;
    public const int MaxNameLength = 40;
    public static readonly string[] Extensions = [".wav", ".mp3", ".ogg"];

    public string LibraryDir { get; } = libraryDir;
    public IReadOnlyList<SoundEntry> Sounds => config.Current.Sounds;

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public SoundEntry? Find(string id) => config.Current.Sounds.Find(s => s.Id == id);

    public static bool FileMissing(SoundEntry s) => s.Sequence is null && !File.Exists(s.FilePath);

    /// <summary>Adds a sequence that plays <paramref name="memberIds"/> one after another.</summary>
    public SoundEntry AddSequence(IReadOnlyList<string> memberIds, string? name = null)
    {
        if (memberIds.Count < 2) throw new ArgumentException("A sequence needs at least two sounds.", nameof(memberIds));
        var members = memberIds.Select(id => Find(id) ?? throw new KeyNotFoundException(id)).ToList();
        if (members.Exists(m => m.Sequence is not null)) throw new ArgumentException("Sequences can't contain other sequences.", nameof(memberIds));
        var entry = new SoundEntry
        {
            Name = CleanName(name ?? string.Join(" → ", members.Select(m => m.Name))),
            Category = members[0].Category,
            Sequence = [.. memberIds],
            Icon = "music",
            Tint = "amber",
        };
        config.Update(c => c.Sounds.Add(entry));
        return entry;
    }

    /// <summary>Sequences that include <paramref name="id"/>.</summary>
    public IReadOnlyList<SoundEntry> SequencesUsing(string id) =>
        config.Current.Sounds.Where(s => s.Sequence?.Contains(id) == true).ToList();

    /// <summary>Copies <paramref name="sourcePath"/> into the library and adds a sound for it.</summary>
    public SoundEntry Import(string sourcePath, string? name = null, string? category = null)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("File not found", sourcePath);
        if (!IsSupported(sourcePath)) throw new NotSupportedException($"{Path.GetFileName(sourcePath)} isn't a WAV, MP3 or OGG file.");
        var target = CopyIntoLibrary(sourcePath);
        return Add(target, name ?? NameFromFile(sourcePath), category, sourceUrl: null);
    }

    /// <summary>Adds a sound whose file is already in the library (e.g. just downloaded).</summary>
    public SoundEntry Add(string libraryFile, string name, string? category, string? sourceUrl)
    {
        var entry = new SoundEntry
        {
            Name = CleanName(name),
            FilePath = libraryFile,
            SourceUrl = sourceUrl,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
        };
        config.Update(c =>
        {
            c.Sounds.Add(entry);
            AddCategory(c, entry.Category);
        });
        return entry;
    }

    /// <summary>Applies an edit. The caller has already validated the hotkey with <see cref="HotkeyRules.Validate"/>.</summary>
    public void Update(string id, Action<SoundEntry> change)
    {
        config.Update(c =>
        {
            var s = c.Sounds.Find(x => x.Id == id) ?? throw new KeyNotFoundException(id);
            change(s);
            s.Name = CleanName(s.Name);
            s.Volume = Math.Clamp(s.Volume, 0, 100);
            s.Category = string.IsNullOrWhiteSpace(s.Category) ? "General" : s.Category.Trim();
            s.Hotkey = Hotkey.TryParse(s.Hotkey, out var hk) ? hk.ToString() : null;
            AddCategory(c, s.Category);
        });
    }

    /// <summary>Points a sound at a new file (copied into the library), e.g. after the old one went missing.</summary>
    public void Relocate(string id, string newSource)
    {
        if (!IsSupported(newSource)) throw new NotSupportedException($"{Path.GetFileName(newSource)} isn't a WAV, MP3 or OGG file.");
        var target = CopyIntoLibrary(newSource);
        Update(id, s => s.FilePath = target);
    }

    /// <summary>Removes the sound; its file is deleted only if it lives in the library folder.</summary>
    public bool Remove(string id)
    {
        SoundEntry? removed = null;
        config.Update(c =>
        {
            removed = c.Sounds.Find(x => x.Id == id);
            if (removed is null) return;
            c.Sounds.Remove(removed);
            c.Recent.Remove(id);
            foreach (var seq in c.Sounds.Where(s => s.Sequence is not null)) seq.Sequence!.RemoveAll(x => x == id);
            c.Sounds.RemoveAll(s => s.Sequence is { Count: < 2 }); // a sequence of one isn't a sequence any more
        });
        if (removed is null) return false;
        if (removed.Sequence is null && IsInLibrary(removed.FilePath) && File.Exists(removed.FilePath) && !config.Current.Sounds.Exists(s => s.FilePath == removed.FilePath))
        {
            try { File.Delete(removed.FilePath); }
            catch (IOException) { } // still in use / locked — harmless leftover
            catch (UnauthorizedAccessException) { }
        }
        return true;
    }

    public void ToggleFavorite(string id) => Update(id, s => s.Favorite = !s.Favorite);

    public void MarkPlayed(string id) => config.Update(c =>
    {
        c.Recent.Remove(id);
        c.Recent.Insert(0, id);
        if (c.Recent.Count > MaxRecent) c.Recent.RemoveRange(MaxRecent, c.Recent.Count - MaxRecent);
    });

    /// <summary>Saved categories plus any used by a sound, in a stable order.</summary>
    public IReadOnlyList<string> Categories()
    {
        var list = new List<string>(config.Current.Categories);
        foreach (var s in config.Current.Sounds)
            if (!list.Contains(s.Category, StringComparer.OrdinalIgnoreCase)) list.Add(s.Category);
        return list;
    }

    public static string NameFromFile(string path)
    {
        var raw = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
        return CleanName(raw);
    }

    private static string CleanName(string name)
    {
        var n = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (n.Length == 0) n = "Untitled sound";
        return n.Length > MaxNameLength ? n[..MaxNameLength] : n;
    }

    private static void AddCategory(AppConfig c, string category)
    {
        if (!c.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)) c.Categories.Add(category);
    }

    private bool IsInLibrary(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(LibraryDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private string CopyIntoLibrary(string source)
    {
        Directory.CreateDirectory(LibraryDir);
        if (IsInLibrary(source)) return Path.GetFullPath(source);
        var name = Path.GetFileNameWithoutExtension(source);
        var bad = Path.GetInvalidFileNameChars();
        name = new string([.. name.Select(ch => bad.Contains(ch) ? '_' : ch)]).Trim();
        if (name.Length == 0) name = "sound";
        var ext = Path.GetExtension(source).ToLowerInvariant();
        var target = Path.Combine(LibraryDir, name + ext);
        for (var i = 2; File.Exists(target); i++) target = Path.Combine(LibraryDir, $"{name} ({i}){ext}");
        File.Copy(source, target);
        return target;
    }
}
