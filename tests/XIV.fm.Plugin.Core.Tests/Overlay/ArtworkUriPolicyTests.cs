using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class ArtworkUriPolicyTests
{
    [Theory]
    [InlineData("https://cdn.example.com/cover.jpg", true)]
    [InlineData("https://cdn.example.com:443/cover.webp?size=large", true)]
    [InlineData("http://cdn.example.com/cover.jpg", false)]
    [InlineData("https://localhost/cover.jpg", false)]
    [InlineData("https://127.0.0.1/cover.jpg", false)]
    [InlineData("https://host.local/cover.jpg", false)]
    [InlineData("https://cdn.example.com:8443/cover.jpg", false)]
    [InlineData("https://user@cdn.example.com/cover.jpg", false)]
    [InlineData("https://cdn.example.com/cover.jpg#fragment", false)]
    public void AllowsOnlyPublicHttpsArtworkLocations(string value, bool expected)
    {
        Assert.Equal(expected, ArtworkUriPolicy.IsAllowed(new Uri(value)));
    }
}
