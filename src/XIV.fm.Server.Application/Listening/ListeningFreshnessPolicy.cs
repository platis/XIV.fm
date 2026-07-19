using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Application.Listening;

public sealed class ListeningFreshnessPolicy
{
    private readonly ListeningPollingOptions options;

    public ListeningFreshnessPolicy(ListeningPollingOptions options)
    {
        this.options = options;
    }

    public TimeSpan GetPollInterval(ListeningObservationStatus status) => status switch
    {
        ListeningObservationStatus.Playing => this.options.PlayingInterval,
        ListeningObservationStatus.NotPlaying => this.options.NotPlayingInterval,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public bool IsStale(ListeningObservation observation, DateTimeOffset now) =>
        now >= observation.ObservedAt.Add(this.GetPollInterval(observation.Status) * 2);
}
