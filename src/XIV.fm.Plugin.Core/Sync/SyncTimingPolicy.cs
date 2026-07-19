namespace XIV.fm.Plugin.Core.Sync;

public static class SyncTimingPolicy
{
    public const int MinimumServerDelaySeconds = 5;
    public const int MaximumServerDelaySeconds = 300;

    public static TimeSpan FromServerDelay(int seconds) =>
        TimeSpan.FromSeconds(Math.Clamp(seconds, MinimumServerDelaySeconds, MaximumServerDelaySeconds));

    public static TimeSpan FailureDelay(int consecutiveFailures)
    {
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 5);
        var seconds = 15 * (1 << exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumServerDelaySeconds));
    }
}
