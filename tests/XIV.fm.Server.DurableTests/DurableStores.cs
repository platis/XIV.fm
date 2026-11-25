using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.DurableTests;

/// <summary>
/// Connects to the disposable stores started by scripts/test-durable.sh. Missing configuration is a failure
/// rather than a skip, so these tests cannot silently stop running.
/// </summary>
public sealed class DurableStores : IAsyncLifetime
{
    public const string PostgresVariable = "XIVFM_TEST_POSTGRES";
    public const string RedisVariable = "XIVFM_TEST_REDIS";

    private DbContextOptions<XivFmDbContext>? options;
    private ConnectionMultiplexer? redis;

    public IDbContextFactory<XivFmDbContext> Contexts => new ContextFactory(
        this.options ?? throw new InvalidOperationException("The stores are not initialized."));

    public IConnectionMultiplexer Redis =>
        this.redis ?? throw new InvalidOperationException("The stores are not initialized.");

    public async Task InitializeAsync()
    {
        this.options = new DbContextOptionsBuilder<XivFmDbContext>()
            .UseNpgsql(GetRequired(PostgresVariable))
            .Options;
        await using var context = await this.Contexts.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        this.redis = await ConnectionMultiplexer.ConnectAsync(GetRequired(RedisVariable));
    }

    public async Task DisposeAsync()
    {
        if (this.redis is not null)
            await this.redis.DisposeAsync();
    }

    public async Task<AccountId> CreateAccountAsync()
    {
        var id = Guid.NewGuid();
        await using var context = await this.Contexts.CreateDbContextAsync();
        context.LastFmAccounts.Add(new LastFmAccountEntity
        {
            AccountId = id,
            CanonicalName = id.ToString("N"),
            NormalizedName = id.ToString("N").ToUpperInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return new AccountId(id);
    }

    private static string GetRequired(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required; run scripts/test-durable.sh.");

    private sealed class ContextFactory(DbContextOptions<XivFmDbContext> options) : IDbContextFactory<XivFmDbContext>
    {
        public XivFmDbContext CreateDbContext() => new(options);
    }
}

[CollectionDefinition(Name)]
public sealed class DurableStoresGroup : ICollectionFixture<DurableStores>
{
    public const string Name = "Durable stores";
}
