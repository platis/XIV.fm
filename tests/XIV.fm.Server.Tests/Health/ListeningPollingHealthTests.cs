using Microsoft.Extensions.Diagnostics.HealthChecks;
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
    public void ShortFailureStreakIsHealthy()
    {
        var health = new ListeningPollingHealth(this.clock);

        this.FailEvery(TimeSpan.FromSeconds(20), ListeningPollingHealth.FailingFor - TimeSpan.FromSeconds(30), health);

        Assert.False(health.IsFailing());
    }

    [Fact]
    public void FailingForFiveMinutesIsFailing()
    {
        var health = new ListeningPollingHealth(this.clock);

        this.FailEvery(TimeSpan.FromSeconds(20), ListeningPollingHealth.FailingFor, health);

        Assert.True(health.IsFailing());
    }

    [Fact]
    public void OneSuccessResetsTheStreak()
    {
        var health = new ListeningPollingHealth(this.clock);
        this.FailEvery(TimeSpan.FromSeconds(20), ListeningPollingHealth.FailingFor, health);

        health.RecordSuccess();
        health.RecordFailure();

        Assert.False(health.IsFailing());
    }

    [Fact]
    public void OutageStaysVisibleBetweenBackedOffRetries()
    {
        var health = new ListeningPollingHealth(this.clock);
        this.FailEvery(TimeSpan.FromSeconds(20), ListeningPollingHealth.FailingFor, health);

        this.clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(health.IsFailing());
    }

    [Fact]
    public void OutageClearsWhenNothingHasFailedForAWhile()
    {
        var health = new ListeningPollingHealth(this.clock);
        this.FailEvery(TimeSpan.FromSeconds(20), ListeningPollingHealth.FailingFor, health);

        this.clock.Advance(ListeningPollingHealth.FailureMemory + TimeSpan.FromSeconds(1));

        Assert.False(health.IsFailing());
    }

    [Fact]
    public async Task HealthCheckReportsDegradedNotUnhealthy()
    {
        var health = new ListeningPollingHealth(this.clock);
        this.FailEvery(TimeSpan.FromSeconds(20), ListeningPollingHealth.FailingFor, health);

        var result = await new ListeningPollingHealthCheck(health).CheckHealthAsync(new());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    private void FailEvery(TimeSpan interval, TimeSpan duration, ListeningPollingHealth health)
    {
        health.RecordFailure();
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += interval)
        {
            this.clock.Advance(interval);
            health.RecordFailure();
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.now;

        public void Advance(TimeSpan by) => this.now += by;
    }
}
