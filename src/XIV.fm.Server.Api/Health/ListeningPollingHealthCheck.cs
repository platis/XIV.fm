using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace XIV.fm.Server.Api.Health;

public sealed class ListeningPollingHealthCheck : IHealthCheck
{
    private readonly ListeningPollingHealth health;

    public ListeningPollingHealthCheck(ListeningPollingHealth health)
    {
        this.health = health;
    }

    // Degraded, not unhealthy: the API keeps serving cached state while Last.fm is down.
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(this.health.IsFailing()
            ? HealthCheckResult.Degraded("Last.fm listening polls have been failing for five minutes.")
            : HealthCheckResult.Healthy());
}
