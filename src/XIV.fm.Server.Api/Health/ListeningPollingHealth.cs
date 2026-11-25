namespace XIV.fm.Server.Api.Health;

/// <summary>
/// Remembers when Last.fm listening polls last succeeded and failed, for the status page.
/// </summary>
public sealed class ListeningPollingHealth
{
    // Polls repeat every 15–20 seconds per active account, so five quiet minutes means a real outage.
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);

    private readonly TimeProvider timeProvider;
    private long lastSuccessTicks;
    private long lastFailureTicks;

    public ListeningPollingHealth(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public void RecordSuccess() =>
        Interlocked.Exchange(ref this.lastSuccessTicks, this.timeProvider.GetUtcNow().UtcTicks);

    public void RecordFailure() =>
        Interlocked.Exchange(ref this.lastFailureTicks, this.timeProvider.GetUtcNow().UtcTicks);

    /// <summary>
    /// True when polls failed recently and none succeeded during the whole window.
    /// No polling at all (nobody online) counts as healthy.
    /// </summary>
    public bool IsFailing()
    {
        var windowStart = this.timeProvider.GetUtcNow().Subtract(FailureWindow).UtcTicks;
        return Interlocked.Read(ref this.lastFailureTicks) > windowStart &&
            Interlocked.Read(ref this.lastSuccessTicks) <= windowStart;
    }
}
