using BreadBoard.Core.Sounds;

namespace BreadBoard.Tests;

public class SoundLinkTests
{
    [Fact]
    public void Myinstants_page()
    {
        Assert.True(SoundLinkParser.TryParse("https://www.myinstants.com/en/instant/vine-boom-sound-70972/", out var link, out _));
        Assert.Equal(SoundLinkKind.MyinstantsPage, link!.Kind);
        Assert.Equal("Vine Boom Sound", link.SuggestedName);
    }

    [Fact]
    public void Direct_file_without_scheme()
    {
        Assert.True(SoundLinkParser.TryParse("example.com/sfx/air_horn.mp3", out var link, out _));
        Assert.Equal(SoundLinkKind.DirectFile, link!.Kind);
        Assert.Equal("Air Horn", link.SuggestedName);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("not a url")]
    [InlineData("ftp://x.com/a.mp3")]
    public void Rejects_unsupported(string input)
    {
        Assert.False(SoundLinkParser.TryParse(input, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
