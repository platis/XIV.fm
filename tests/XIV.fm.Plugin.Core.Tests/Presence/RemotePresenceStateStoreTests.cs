using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Presence;
using ContractLocationScope = XIV.fm.Contracts.V1.LocationScope;

namespace XIV.fm.Plugin.Core.Tests.Presence;

public sealed class RemotePresenceStateStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly ContractLocationScope Location = new(63, 129, 130, 2);

    [Fact]
    public void UnchangedVersionRetainsCardsOnlyUntilSnapshotExpiry()
    {
        var store = new RemotePresenceStateStore();
        var snapshot = CreateSnapshot(Location, Now.AddSeconds(20));

        Assert.True(store.Apply(new SnapshotResult("v1", snapshot), Location, Now));
        Assert.Single(store.Read(Now));
        Assert.True(store.Apply(new SnapshotResult("v1", null), Location, Now.AddSeconds(5)));
        Assert.Single(store.Read(Now.AddSeconds(19)));
        Assert.Empty(store.Read(Now.AddSeconds(20)));
    }

    [Fact]
    public void UnexpectedLocationFailsClosedAndClearsPriorCards()
    {
        var store = new RemotePresenceStateStore();
        Assert.True(store.Apply(
            new SnapshotResult("v1", CreateSnapshot(Location, Now.AddSeconds(20))),
            Location,
            Now));

        var accepted = store.Apply(
            new SnapshotResult(
                "v2",
                CreateSnapshot(new ContractLocationScope(63, 129, 130, 3), Now.AddSeconds(20))),
            Location,
            Now);

        Assert.False(accepted);
        Assert.Empty(store.Read(Now));
    }

    [Fact]
    public void LifecycleClearRemovesCardsImmediately()
    {
        var store = new RemotePresenceStateStore();
        store.Apply(
            new SnapshotResult("v1", CreateSnapshot(Location, Now.AddSeconds(20))),
            Location,
            Now);

        store.Clear();

        Assert.Empty(store.Read(Now));
    }

    private static PresenceSnapshot CreateSnapshot(ContractLocationScope location, DateTimeOffset expiresAt) => new(
        location,
        Now,
        expiresAt,
        [
            new PresenceEntry(
                new CharacterIdentity("Bob Bun", 55),
                new ListeningState(
                    ListeningStatus.Playing,
                    false,
                    Now,
                    new Track("Track", "Artist", null, null, null, null))),
        ]);
}
