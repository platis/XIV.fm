using XIV.fm.Server.Api.Health;

namespace XIV.fm.Server.Tests.Health;

public sealed class ListeningPollingHealthTests
{
    private readonly Clock clock = new();

    [Fact]
    public void NoPollingIsHealthy()
    {
        var health = new ListeningPollingHealth(this.clock);

        this.clock.Advance(TimeSpan.FromHours(1));

        Assert.False(health.IsFailing());
    }

    [Fact]
    public void FailureAfterRecentSuccessIsHealthy()
    {
        var health = new ListeningPollingHealth(this.clock);
        health.RecordSuccess();
        this.clock.Advance(TimeSpan.FromMinutes(1));

        health.RecordFailure();

        Assert.False(health.IsFailing());
    }

    [Fact]
    public void FailuresWithoutSuccessForTheWholeWindowAreFailing()
    {
        var health = new ListeningPollingHealth(this.clock);
        health.RecordSuccess();
        this.clock.Advance(ListeningPollingHealth.FailureWindow + TimeSpan.FromSeconds(1));

        health.RecordFailure();

        Assert.True(health.IsFailing());
    }

    [Fact]
    public void FailingClearsAfterSuccess()
    {
        var health = new ListeningPollingHealth(this.clock);
        health.RecordFailure();
        Assert.True(health.IsFailing());

        health.RecordSuccess();

        Assert.False(health.IsFailing());
    }

    [Fact]
    public void OldFailuresAreForgotten()
    {
        var health = new ListeningPollingHealth(this.clock);
        health.RecordFailure();

        this.clock.Advance(ListeningPollingHealth.FailureWindow + TimeSpan.FromSeconds(1));

        Assert.False(health.IsFailing());
    }

    [Fact]
    public async Task HealthCheckReportsDegradedNotUnhealthy()
    {
        var health = new ListeningPollingHealth(this.clock);
        health.RecordFailure();

        var result = await new ListeningPollingHealthCheck(health).CheckHealthAsync(new());

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.now;

        public void Advance(TimeSpan by) => this.now += by;
    }
}
