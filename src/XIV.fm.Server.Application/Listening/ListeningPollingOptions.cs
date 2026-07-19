namespace XIV.fm.Server.Application.Listening;

public sealed record ListeningPollingOptions(
    TimeSpan PlayingInterval,
    TimeSpan NotPlayingInterval,
    TimeSpan MaximumBackoff,
    int CircuitFailureThreshold,
    TimeSpan CircuitBreakDuration)
{
    public static ListeningPollingOptions Default { get; } = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromMinutes(5),
        5,
        TimeSpan.FromMinutes(2));
}
