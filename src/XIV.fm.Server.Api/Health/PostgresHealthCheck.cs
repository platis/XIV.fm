using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.Api.Health;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<XivFmDbContext> contextFactory;

    public PostgresHealthCheck(IDbContextFactory<XivFmDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var database = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await database.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.", exception);
        }
    }
}
