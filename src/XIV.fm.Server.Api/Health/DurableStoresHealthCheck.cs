using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.Api.Health;

public sealed class DurableStoresHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<XivFmDbContext> contextFactory;
    private readonly IConnectionMultiplexer redis;

    public DurableStoresHealthCheck(
        IDbContextFactory<XivFmDbContext> contextFactory,
        IConnectionMultiplexer redis)
    {
        this.contextFactory = contextFactory;
        this.redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var database = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            if (!await database.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
                return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");

            await this.redis.GetDatabase().PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("A durable store is unavailable.", exception);
        }
    }
}
