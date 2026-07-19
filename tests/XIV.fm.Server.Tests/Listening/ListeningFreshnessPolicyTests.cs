using XIV.fm.Server.Application.Listening;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Tests.Listening;

public sealed class ListeningFreshnessPolicyTests
{
    private readonly ListeningFreshnessPolicy policy = new(ListeningPollingOptions.Default);

    [Theory]
    [InlineData(ListeningObservationStatus.Playing, 59, false)]
    [InlineData(ListeningObservationStatus.Playing, 60, true)]
    [InlineData(ListeningObservationStatus.NotPlaying, 179, false)]
    [InlineData(ListeningObservationStatus.NotPlaying, 180, true)]
    public void CacheBecomesStaleAfterTwoTargetIntervals(
        ListeningObservationStatus status,
        int ageSeconds,
        bool expected)
    {
        var observedAt = new DateTimeOffset(2026, 7, 19, 11, 0, 0, TimeSpan.Zero);
        var track = status == ListeningObservationStatus.Playing
            ? new NormalizedTrack("Track", "Artist", null, null, null, null)
            : null;
        var observation = new ListeningObservation(status, observedAt, track);

        Assert.Equal(expected, this.policy.IsStale(observation, observedAt.AddSeconds(ageSeconds)));
    }

    [Fact]
    public void UsesAdaptivePlayingAndNotPlayingIntervals()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            this.policy.GetPollInterval(ListeningObservationStatus.Playing));
        Assert.Equal(
            TimeSpan.FromSeconds(90),
            this.policy.GetPollInterval(ListeningObservationStatus.NotPlaying));
    }
}
