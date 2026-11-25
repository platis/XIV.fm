using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Presence;

namespace XIV.fm.Plugin.Core.Tests.Presence;

public sealed class OwnListeningStateStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ListeningState Playing = new(
        ListeningStatus.Playing,
        false,
        Now,
        new Track("Track", "Artist", null, null, null, null));

    [Fact]
    public void StateIsUnavailableBeforeTheFirstSync()
    {
        Assert.Equal(ListeningStatus.Unavailable, new OwnListeningStateStore().Read(Now).Status);
    }

    [Fact]
    public void FailedSyncsDoNotKeepTheLastTrackForever()
    {
        var store = new OwnListeningStateStore();
        Assert.True(store.Apply(Playing, Now));

        Assert.Equal(Playing, store.Read(Now.AddSeconds(119)));
        Assert.Equal(ListeningStatus.Unavailable, store.Read(Now.AddMinutes(2)).Status);
    }

    [Fact]
    public void EverySuccessfulSyncExtendsAnUnchangedTrack()
    {
        var store = new OwnListeningStateStore();
        Assert.True(store.Apply(Playing, Now));
        Assert.False(store.Apply(Playing, Now.AddSeconds(90)));

        Assert.Equal(Playing, store.Read(Now.AddSeconds(180)));
    }

    [Fact]
    public void RecoveryAfterExpiryReportsAChange()
    {
        var store = new OwnListeningStateStore();
        store.Apply(Playing, Now);

        Assert.True(store.Apply(Playing, Now.AddMinutes(5)));
    }

    [Fact]
    public void ClearRemovesTheTrackOnce()
    {
        var store = new OwnListeningStateStore();
        store.Apply(Playing, Now);

        Assert.True(store.Clear(Now));
        Assert.Equal(ListeningStatus.Unavailable, store.Read(Now).Status);
        Assert.False(store.Clear(Now));
    }
}
