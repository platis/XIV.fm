using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class OverlayStateStoreTests
{
    [Fact]
    public void NewStoreStartsWithEmptySnapshot()
    {
        var store = new OverlayStateStore();

        Assert.Same(OverlaySnapshot.Empty, store.Current);
    }

    [Fact]
    public void PublishReplacesTheCompleteSnapshot()
    {
        var store = new OverlayStateStore();
        var first = OverlaySnapshot.Create(
            [OverlayCard.LocalPlaceholder(new CharacterIdentity("Alice Cat", 54))],
            new DateTimeOffset(2026, 7, 18, 20, 0, 0, TimeSpan.Zero));
        var second = OverlaySnapshot.Create(
            [OverlayCard.LocalPlaceholder(new CharacterIdentity("Bob Cat", 63))],
            new DateTimeOffset(2026, 7, 18, 20, 0, 1, TimeSpan.Zero));

        store.Publish(first);
        store.Publish(second);

        Assert.Same(second, store.Current);
        Assert.Equal("Bob Cat", store.Current.Cards.Single().Character.Name);
    }

    [Fact]
    public void CreateCopiesCardsIntoImmutableStorage()
    {
        var source = new List<OverlayCard>
        {
            OverlayCard.LocalPlaceholder(new CharacterIdentity("Alice Cat", 54)),
        };

        var snapshot = OverlaySnapshot.Create(source, DateTimeOffset.UtcNow);
        source.Clear();

        Assert.Single(snapshot.Cards);
    }

    [Fact]
    public void EmptySnapshotContainsNoCards()
    {
        Assert.True(OverlaySnapshot.Empty.Cards.IsEmpty);
    }
}
