namespace XIV.fm.Server.Api.Health;

/// <summary>
/// Tracks whether Last.fm listening polls have been failing without a single success, for the status page.
/// </summary>
public sealed class ListeningPollingHealth
{
    // Polls repeat every 15–20 seconds per active account, so five failing minutes means a real outage.
    public static readonly TimeSpan FailingFor = TimeSpan.FromMinutes(5);

    // Failing accounts back off for up to five minutes, so an outage stays visible this long after its last failure.
    public static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(15);

    private readonly TimeProvider timeProvider;
    private readonly Lock gate = new();
    private DateTimeOffset? failingSince;
    private DateTimeOffset lastFailure;

    public ListeningPollingHealth(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public void RecordSuccess()
    {
        lock (this.gate)
            this.failingSince = null;
    }

    public void RecordFailure()
    {
        var now = this.timeProvider.GetUtcNow();
        lock (this.gate)
        {
            this.failingSince ??= now;
            this.lastFailure = now;
        }
    }

    /// <summary>
    /// True when every poll for at least five minutes failed and the last failure is recent.
    /// No polling at all (nobody online) counts as healthy.
    /// </summary>
    public bool IsFailing()
    {
        var now = this.timeProvider.GetUtcNow();
        lock (this.gate)
        {
            return this.failingSince is { } since &&
                now - since >= FailingFor &&
                now - this.lastFailure <= FailureMemory;
        }
    }
}
