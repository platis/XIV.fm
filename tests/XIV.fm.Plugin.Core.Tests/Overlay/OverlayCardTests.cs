using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class OverlayCardTests
{
    private static readonly XIV.fm.Plugin.Core.Overlay.CharacterIdentity Character =
        new("Alice Cat", 54);
    private static readonly DateTimeOffset Now =
        new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlayingStateDrivesLocalTrackCard()
    {
        var listening = new ListeningState(
            ListeningStatus.Playing,
            false,
            Now.AddSeconds(-10),
            new Track("Track", "Artist", "Album", null, null, null));

        var card = OverlayCard.LocalListening(Character, listening, Now);

        Assert.Equal("Track", card.Title);
        Assert.Equal("Artist", card.Artist);
        Assert.True(card.IsLocal);
        Assert.False(card.IsStale);
    }

    [Fact]
    public void PublicSnapshotListeningStateCreatesRemoteCard()
    {
        var listening = new ListeningState(
            ListeningStatus.Playing,
            false,
            Now,
            new Track("Remote Track", "Remote Artist", null, null, null, null));

        var card = OverlayCard.RemoteListening(Character, listening, Now);

        Assert.False(card.IsLocal);
        Assert.True(card.IsLastFm);
        Assert.Equal("Remote Track", card.Title);
    }

    [Fact]
    public void DisconnectedPlayingStateBecomesLocallyStale()
    {
        var listening = new ListeningState(
            ListeningStatus.Playing,
            false,
            Now.AddSeconds(-60),
            new Track("Track", "Artist", null, null, null, null));

        var card = OverlayCard.LocalListening(Character, listening, Now);

        Assert.True(card.IsStale);
    }

    [Fact]
    public void NotPlayingAndUnavailableHaveExplicitLocalStates()
    {
        var notPlaying = OverlayCard.LocalListening(
            Character,
            new ListeningState(ListeningStatus.NotPlaying, false, Now, null),
            Now);
        var unavailable = OverlayCard.LocalListening(
            Character,
            new ListeningState(ListeningStatus.Unavailable, false, null, null),
            Now);

        Assert.Equal("Nothing playing", notPlaying.Title);
        Assert.Equal("Listening unavailable", unavailable.Title);
        Assert.True(unavailable.IsStale);
    }
}
