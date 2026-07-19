using System.Diagnostics.Metrics;

namespace XIV.fm.Server.Api.Telemetry;

public sealed class XivFmTelemetry : IDisposable
{
    public const string MeterName = "XIV.fm.Server";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> syncRequests;
    private readonly Counter<long> syncSuccesses;
    private readonly Counter<long> authenticationFailures;
    private readonly Counter<long> rateLimitedRequests;

    public XivFmTelemetry()
    {
        this.syncRequests = this.meter.CreateCounter<long>("xivfm.sync.requests");
        this.syncSuccesses = this.meter.CreateCounter<long>("xivfm.sync.successes");
        this.authenticationFailures = this.meter.CreateCounter<long>("xivfm.auth.failures");
        this.rateLimitedRequests = this.meter.CreateCounter<long>("xivfm.http.rate_limited");
    }

    public void RecordSyncRequest() => this.syncRequests.Add(1);

    public void RecordSyncSuccess() => this.syncSuccesses.Add(1);

    public void RecordAuthenticationFailure() => this.authenticationFailures.Add(1);

    public void RecordRateLimitedRequest() => this.rateLimitedRequests.Add(1);

    public void Dispose() => this.meter.Dispose();
}
