using System.Net;
using System.Text;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;
using XIV.fm.Server.Infrastructure.LastFm;

namespace XIV.fm.Server.Tests.LastFm;

public sealed class LastFmRecentTracksClientTests
{
    [Fact]
    public async Task MapsNowPlayingTrackToBoundedNormalizedObservation()
    {
        const string json = """
            {
              "recenttracks": {
                "track": [{
                  "name": "  Test Track  ",
                  "artist": { "#text": "Test Artist" },
                  "album": { "#text": "Test Album" },
                  "url": "https://www.last.fm/music/test",
                  "image": [
                    { "#text": "https://images.last.fm/small.jpg", "size": "small" },
                    { "#text": "https://images.last.fm/large.jpg", "size": "large" }
                  ],
                  "@attr": { "nowplaying": "true" }
                }]
              }
            }
            """;
        var now = new DateTimeOffset(2026, 7, 19, 11, 0, 0, TimeSpan.Zero);
        using var httpClient = new HttpClient(new JsonHandler(json));
        var client = CreateClient(httpClient, new FixedTimeProvider(now));

        var observation = await client.GetCurrentAsync(
            new LastFmAccountIdentity("CanonicalListener"),
            CancellationToken.None);

        Assert.Equal(ListeningObservationStatus.Playing, observation.Status);
        Assert.Equal(now, observation.ObservedAt);
        Assert.NotNull(observation.Track);
        Assert.Equal("Test Track", observation.Track.Title);
        Assert.Equal("Test Artist", observation.Track.Artist);
        Assert.Null(observation.Track.AlbumArtUrl);
    }

    [Fact]
    public async Task LatestHistoricalTrackMapsToNotPlayingWithoutPublishingTrack()
    {
        const string json = """
            { "recenttracks": { "track": [{
              "name": "Old Track",
              "artist": { "#text": "Artist" },
              "date": { "uts": "1752910000" }
            }] } }
            """;
        using var httpClient = new HttpClient(new JsonHandler(json));
        var client = CreateClient(httpClient, TimeProvider.System);

        var observation = await client.GetCurrentAsync(
            new LastFmAccountIdentity("CanonicalListener"),
            CancellationToken.None);

        Assert.Equal(ListeningObservationStatus.NotPlaying, observation.Status);
        Assert.Null(observation.Track);
    }

    [Fact]
    public async Task RejectsOversizedProviderTrackInsteadOfExpandingTheCacheContract()
    {
        var title = new string('x', 513);
        var json = $$"""
            { "recenttracks": { "track": [{
              "name": "{{title}}",
              "artist": { "#text": "Artist" },
              "@attr": { "nowplaying": "true" }
            }] } }
            """;
        using var httpClient = new HttpClient(new JsonHandler(json));
        var client = CreateClient(httpClient, TimeProvider.System);

        await Assert.ThrowsAsync<LastFmRecentTracksException>(() =>
            client.GetCurrentAsync(
                new LastFmAccountIdentity("CanonicalListener"),
                CancellationToken.None).AsTask());
    }

    private static LastFmRecentTracksClient CreateClient(HttpClient httpClient, TimeProvider timeProvider) =>
        new(
            httpClient,
            new LastFmAuthorizationOptions(
                "api-key",
                "unused-for-read-only-polling",
                LastFmAuthorizationOptions.DefaultApiBaseUri,
                LastFmAuthorizationOptions.DefaultBrowserBaseUri),
            new ImmediateBudget(),
            timeProvider);

    private sealed class ImmediateBudget : ILastFmRequestBudget
    {
        public ValueTask AcquireAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => this.now;
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string json;

        public JsonHandler(string json)
        {
            this.json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotNull(request.RequestUri);
            Assert.Contains("method=user.getRecentTracks", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("limit=1", request.RequestUri.Query, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
