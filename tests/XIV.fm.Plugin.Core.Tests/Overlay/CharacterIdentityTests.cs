using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class CharacterIdentityTests
{
    [Fact]
    public void MatchesNameCaseInsensitivelyOnTheSameWorld()
    {
        var left = new CharacterIdentity("Alice Cat", 54);
        var right = new CharacterIdentity("alice cat", 54);

        Assert.True(left.Matches(right));
    }

    [Fact]
    public void DoesNotMatchKnownDifferentWorlds()
    {
        var left = new CharacterIdentity("Alice Cat", 54);
        var right = new CharacterIdentity("Alice Cat", 63);

        Assert.False(left.Matches(right));
    }

    [Fact]
    public void UnknownWorldFailsClosedInsteadOfMatchingByNameOnly()
    {
        var known = new CharacterIdentity("Alice Cat", 54);
        var unknown = new CharacterIdentity("Alice Cat", 0);

        Assert.False(known.Matches(unknown));
    }
}
