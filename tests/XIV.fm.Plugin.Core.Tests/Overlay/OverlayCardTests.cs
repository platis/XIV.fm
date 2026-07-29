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

        Assert.NotNull(card);
        Assert.Equal("Track", card.Title);
        Assert.Equal("Artist", card.Artist);
        Assert.True(card.IsLocal);
        Assert.False(card.IsStale);
    }

    [Fact]
    public void PlayingStateCarriesOnlySafeHttpsArtwork()
    {
        var safeArtwork = new Uri("https://cdn.example.com/cover.png");
        var safe = OverlayCard.LocalListening(
            Character,
            new ListeningState(
                ListeningStatus.Playing,
                false,
                Now,
                new Track("Track", "Artist", "Album", safeArtwork, null, null)),
            Now);
        var unsafeCard = OverlayCard.LocalListening(
            Character,
            new ListeningState(
                ListeningStatus.Playing,
                false,
                Now,
                new Track("Track", "Artist", "Album", new Uri("http://127.0.0.1/cover.png"), null, null)),
            Now);

        Assert.NotNull(safe);
        Assert.NotNull(unsafeCard);
        Assert.Equal(safeArtwork, safe.ArtworkUrl);
        Assert.Null(unsafeCard.ArtworkUrl);
    }

    [Fact]
    public void ChangedListeningStateAdvancesTitleAndArtworkTogether()
    {
        var firstArtwork = new Uri("https://cdn.example.com/first.png");
        var secondArtwork = new Uri("https://cdn.example.com/second.png");
        var first = OverlayCard.LocalListening(
            Character,
            new ListeningState(
                ListeningStatus.Playing,
                false,
                Now,
                new Track("First", "Artist", "Album", firstArtwork, null, null)),
            Now);
        var second = OverlayCard.LocalListening(
            Character,
            new ListeningState(
                ListeningStatus.Playing,
                false,
                Now.AddSeconds(30),
                new Track("Second", "Artist", "Album", secondArtwork, null, null)),
            Now.AddSeconds(30));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("First", first.Title);
        Assert.Equal(firstArtwork, first.ArtworkUrl);
        Assert.Equal("Second", second.Title);
        Assert.Equal(secondArtwork, second.ArtworkUrl);
    }

    [Fact]
    public void PublicSnapshotListeningStateCreatesRemoteCardWithSafeTrackLink()
    {
        var trackUrl = new Uri("https://www.last.fm/music/remote/track");
        var listening = new ListeningState(
            ListeningStatus.Playing,
            false,
            Now,
            new Track("Remote Track", "Remote Artist", null, null, trackUrl, null));

        var card = OverlayCard.RemoteListening(Character, listening, Now);

        Assert.NotNull(card);
        Assert.False(card.IsLocal);
        Assert.True(card.IsLastFm);
        Assert.Equal("Remote Track", card.Title);
        Assert.Equal(trackUrl, card.TrackUrl);
    }

    [Fact]
    public void NonLastFmTrackLinkIsNotInteractive()
    {
        var card = OverlayCard.LocalListening(
            Character,
            new ListeningState(
                ListeningStatus.Playing,
                false,
                Now,
                new Track("Track", "Artist", null, null, new Uri("https://example.com/track"), null)),
            Now);

        Assert.NotNull(card);
        Assert.Null(card.TrackUrl);
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

        Assert.NotNull(card);
        Assert.True(card.IsStale);
    }

    [Fact]
    public void NotPlayingAndUnavailableDoNotCreateCards()
    {
        var notPlaying = OverlayCard.LocalListening(
            Character,
            new ListeningState(ListeningStatus.NotPlaying, false, Now, null),
            Now);
        var unavailable = OverlayCard.LocalListening(
            Character,
            new ListeningState(ListeningStatus.Unavailable, false, null, null),
            Now);

        Assert.Null(notPlaying);
        Assert.Null(unavailable);
    }
}
