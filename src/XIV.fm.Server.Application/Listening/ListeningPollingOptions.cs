namespace XIV.fm.Server.Application.Listening;

public sealed record ListeningPollingOptions(
    TimeSpan PlayingInterval,
    TimeSpan NotPlayingInterval,
    TimeSpan PlayingStaleAfter,
    TimeSpan NotPlayingStaleAfter,
    TimeSpan MaximumBackoff,
    int CircuitFailureThreshold,
    TimeSpan CircuitBreakDuration)
{
    public static ListeningPollingOptions Default { get; } = new(
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(180),
        TimeSpan.FromMinutes(5),
        5,
        TimeSpan.FromMinutes(2));
}
