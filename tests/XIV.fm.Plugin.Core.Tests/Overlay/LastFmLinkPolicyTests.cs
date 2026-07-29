using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class LastFmLinkPolicyTests
{
    [Theory]
    [InlineData("https://last.fm/music/artist/track")]
    [InlineData("https://www.last.fm/music/artist/track")]
    [InlineData("https://www.last.fm:443/music/artist/track")]
    public void AllowsLastFmHttpsLinks(string value)
    {
        Assert.True(LastFmLinkPolicy.IsAllowed(new Uri(value)));
    }

    [Theory]
    [InlineData("http://www.last.fm/music/artist/track")]
    [InlineData("https://last.fm.example.com/music/artist/track")]
    [InlineData("https://example.com/music/artist/track")]
    [InlineData("https://user@www.last.fm/music/artist/track")]
    [InlineData("https://www.last.fm:8443/music/artist/track")]
    public void RejectsUntrustedTrackLinks(string value)
    {
        Assert.False(LastFmLinkPolicy.IsAllowed(new Uri(value)));
    }
}
