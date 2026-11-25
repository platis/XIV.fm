using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class RemoteCardLookupTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FindsRemoteCardsByNameAndHomeWorldOnly()
    {
        var lookup = new RemoteCardLookup(OverlaySnapshot.Create(
        [
            Card("Local Cat", 54, isLocal: true),
            Card("Alice Cat", 54),
            Card("Alice Cat", 55),
            Card("No World", 0),
        ],
        Now));

        Assert.True(lookup.TryFind(new CharacterIdentity("ALICE CAT", 54), out var first));
        Assert.Equal(1, first);
        Assert.True(lookup.TryFind(new CharacterIdentity("Alice Cat", 55), out var second));
        Assert.Equal(2, second);
        Assert.False(lookup.TryFind(new CharacterIdentity("Alice Cat", 56), out _));
        Assert.False(lookup.TryFind(new CharacterIdentity("Local Cat", 54), out _));
        Assert.False(lookup.TryFind(new CharacterIdentity("No World", 0), out _));
    }

    [Fact]
    public void DuplicateCharactersUseOneCard()
    {
        var lookup = new RemoteCardLookup(OverlaySnapshot.Create(
            [Card("Alice Cat", 54), Card("alice cat", 54)],
            Now));

        Assert.True(lookup.TryFind(new CharacterIdentity("Alice Cat", 54), out var index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void IdentitiesAreDecodedOncePerLifetime()
    {
        var identities = new LoadedPlayerIdentities();
        var decoded = 0;
        CharacterIdentity? Decode(string name)
        {
            decoded++;
            return new CharacterIdentity(name, 54);
        }

        for (var frame = 0; frame < 100; frame++)
        {
            identities.BeginFrame(Now.AddMilliseconds(frame * 16));
            Assert.Equal("Alice Cat", identities.Get(1, "Alice Cat", Decode)!.Value.Name);
        }

        Assert.Equal(1, decoded);
        identities.BeginFrame(Now.AddSeconds(5));
        identities.Get(1, "Alice Cat", Decode);
        Assert.Equal(2, decoded);
    }

    [Fact]
    public void UnavailableIdentitiesAreRetried()
    {
        var identities = new LoadedPlayerIdentities();
        identities.BeginFrame(Now);

        Assert.Null(identities.Get(1, string.Empty, _ => null));
        Assert.NotNull(identities.Get(1, "Alice Cat", name => new CharacterIdentity(name, 54)));
    }

    [Fact]
    public void ArtworkIsKeptBrieflyAfterAPlayerLeaves()
    {
        var tracker = new NearbyArtworkTracker();
        var first = new Uri("https://example.com/first.png");
        var second = new Uri("https://example.com/second.png");
        tracker.Observe(first, Now);
        tracker.Observe(second, Now.AddSeconds(20));

        Assert.Equal([second, first], tracker.GetCurrent(Now.AddSeconds(29)));
        Assert.Equal([second], tracker.GetCurrent(Now.AddSeconds(30)));
        Assert.Empty(tracker.GetCurrent(Now.AddSeconds(50)));
    }

    private static OverlayCard Card(string name, uint world, bool isLocal = false) =>
        new(new CharacterIdentity(name, world), "Track", "Artist", isLocal);
}
