using System.Diagnostics.Metrics;
using XIV.fm.Server.Api.Health;
using XIV.fm.Server.Application.Abstractions;

namespace XIV.fm.Server.Api.Telemetry;

public sealed partial class XivFmTelemetry : IListeningPollingTelemetry, IPresenceSnapshotTelemetry, IRelayTelemetry, IDisposable
{
    public const string MeterName = "XIV.fm.Server";

    private readonly Meter meter = new(MeterName);
    private readonly ILogger<XivFmTelemetry> logger;
    private readonly ListeningPollingHealth pollingHealth;
    private readonly Counter<long> syncRequests;
    private readonly Counter<long> syncSuccesses;
    private readonly Counter<long> authenticationFailures;
    private readonly Counter<long> rateLimitedRequests;
    private readonly Counter<long> listeningCacheHits;
    private readonly Counter<long> listeningCacheMisses;
    private readonly Counter<long> listeningPollSuccesses;
    private readonly Counter<long> listeningPollFailures;
    private readonly Counter<long> listeningLeaseContentions;
    private readonly Counter<long> snapshotCacheHits;
    private readonly Counter<long> snapshotCacheMisses;
    private readonly Counter<long> snapshotBuilds;
    private readonly Histogram<int> snapshotEntryCounts;
    private readonly Counter<long> relaysCreated;
    private readonly Counter<long> relaysDeleted;
    private readonly Counter<long> relayInvitationsCreated;
    private readonly Counter<long> relayJoins;
    private readonly Counter<long> relayLeaves;
    private readonly Counter<long> relayKicks;

    public XivFmTelemetry(ILogger<XivFmTelemetry> logger, ListeningPollingHealth pollingHealth)
    {
        this.logger = logger;
        this.pollingHealth = pollingHealth;
        this.syncRequests = this.meter.CreateCounter<long>("xivfm.sync.requests");
        this.syncSuccesses = this.meter.CreateCounter<long>("xivfm.sync.successes");
        this.authenticationFailures = this.meter.CreateCounter<long>("xivfm.auth.failures");
        this.rateLimitedRequests = this.meter.CreateCounter<long>("xivfm.http.rate_limited");
        this.listeningCacheHits = this.meter.CreateCounter<long>("xivfm.listening.cache_hits");
        this.listeningCacheMisses = this.meter.CreateCounter<long>("xivfm.listening.cache_misses");
        this.listeningPollSuccesses = this.meter.CreateCounter<long>("xivfm.listening.poll_successes");
        this.listeningPollFailures = this.meter.CreateCounter<long>("xivfm.listening.poll_failures");
        this.listeningLeaseContentions = this.meter.CreateCounter<long>("xivfm.listening.lease_contentions");
        this.snapshotCacheHits = this.meter.CreateCounter<long>("xivfm.snapshot.cache_hits");
        this.snapshotCacheMisses = this.meter.CreateCounter<long>("xivfm.snapshot.cache_misses");
        this.snapshotBuilds = this.meter.CreateCounter<long>("xivfm.snapshot.builds");
        this.snapshotEntryCounts = this.meter.CreateHistogram<int>("xivfm.snapshot.entry_count");
        this.relaysCreated = this.meter.CreateCounter<long>("xivfm.relay.created");
        this.relaysDeleted = this.meter.CreateCounter<long>("xivfm.relay.deleted");
        this.relayInvitationsCreated = this.meter.CreateCounter<long>("xivfm.relay.invitation_created");
        this.relayJoins = this.meter.CreateCounter<long>("xivfm.relay.joined");
        this.relayLeaves = this.meter.CreateCounter<long>("xivfm.relay.left");
        this.relayKicks = this.meter.CreateCounter<long>("xivfm.relay.kicked");
    }

    public void RecordSyncRequest() => this.syncRequests.Add(1);

    public void RecordSyncSuccess() => this.syncSuccesses.Add(1);

    public void RecordAuthenticationFailure() => this.authenticationFailures.Add(1);

    public void RecordRateLimitedRequest() => this.rateLimitedRequests.Add(1);

    public void RecordCacheRead(bool found) =>
        (found ? this.listeningCacheHits : this.listeningCacheMisses).Add(1);

    public void RecordPollSuccess()
    {
        this.listeningPollSuccesses.Add(1);
        this.pollingHealth.RecordSuccess();
    }

    public void RecordPollFailure(Exception exception)
    {
        this.listeningPollFailures.Add(1);
        this.pollingHealth.RecordFailure();
        LogPollFailure(this.logger, exception.GetType().Name, exception.InnerException?.GetType().Name);
    }

    public void RecordLeaseContention() => this.listeningLeaseContentions.Add(1);

    public void RecordSnapshotCacheRead(bool found) =>
        (found ? this.snapshotCacheHits : this.snapshotCacheMisses).Add(1);

    public void RecordSnapshotBuild(int entryCount)
    {
        this.snapshotBuilds.Add(1);
        this.snapshotEntryCounts.Record(entryCount);
    }

    public void RecordCreated() => this.relaysCreated.Add(1);

    public void RecordDeleted() => this.relaysDeleted.Add(1);

    public void RecordInvitationCreated() => this.relayInvitationsCreated.Add(1);

    public void RecordJoined() => this.relayJoins.Add(1);

    public void RecordLeft() => this.relayLeaves.Add(1);

    public void RecordKicked() => this.relayKicks.Add(1);

    public void Dispose() => this.meter.Dispose();

    // Only exception types are logged; messages could carry provider or account details.
    [LoggerMessage(1, LogLevel.Warning, "A listening poll failed: {ExceptionType} (cause: {CauseType}).")]
    private static partial void LogPollFailure(ILogger logger, string exceptionType, string? causeType);
}
