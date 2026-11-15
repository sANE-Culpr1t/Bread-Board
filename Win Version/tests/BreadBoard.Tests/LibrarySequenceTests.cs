using BreadBoard.Core.Config;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.Tests;

public sealed class LibrarySequenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bb-seq-" + Guid.NewGuid().ToString("N"));
    private readonly ConfigService _config;
    private readonly SoundLibrary _lib;

    public LibrarySequenceTests()
    {
        _config = new ConfigService(new ConfigStore(Path.Combine(_dir, "config.json")), debounceMs: 50);
        _lib = new SoundLibrary(_config, Path.Combine(_dir, "Sounds"));
    }

    private SoundEntry Sound(string name) => _lib.Import(TestWav.Write(Path.Combine(_dir, "src", name + ".wav")));

    [Fact]
    public void Sequences_are_named_validated_and_follow_their_members()
    {
        var a = Sound("alpha"); var b = Sound("beta"); var c = Sound("gamma");
        var seq = _lib.AddSequence([a.Id, b.Id, c.Id]);
        Assert.Equal("alpha → beta → gamma", seq.Name);
        Assert.False(SoundLibrary.FileMissing(seq)); // no file of its own
        Assert.Throws<ArgumentException>(() => _lib.AddSequence([a.Id]));
        Assert.Throws<ArgumentException>(() => _lib.AddSequence([seq.Id, a.Id])); // no nesting
        Assert.Single(_lib.SequencesUsing(b.Id));

        _lib.Remove(b.Id);
        Assert.Equal([a.Id, c.Id], _lib.Find(seq.Id)!.Sequence);
        _lib.Remove(c.Id);
        Assert.Null(_lib.Find(seq.Id)); // one sound left: no longer a sequence
        Assert.True(File.Exists(a.FilePath));
    }

    public void Dispose()
    {
        _config.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
