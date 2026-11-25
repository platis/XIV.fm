using System.Collections.Concurrent;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Infrastructure.AccountLinks;

/// <summary>
/// Remembers which account an installation belongs to. That association never changes, and every request
/// still authenticates against PostgreSQL first, so a revoked or deleted installation never reaches this cache.
/// </summary>
public sealed class CachedLinkedAccountResolver : ILinkedAccountResolver
{
    private const int MaximumEntries = 10_000;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<InstallationId, Entry> entries = new();
    private readonly PostgresAccountLinkStore store;
    private readonly TimeProvider timeProvider;

    public CachedLinkedAccountResolver(PostgresAccountLinkStore store, TimeProvider timeProvider)
    {
        this.store = store;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<LinkedLastFmAccount?> GetForInstallationAsync(
        InstallationId installationId,
        CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        if (this.entries.TryGetValue(installationId, out var entry) && entry.ExpiresAt > now)
            return entry.Account;

        var account = await this.store
            .GetForInstallationAsync(installationId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            this.entries.TryRemove(installationId, out _);
            return null;
        }

        if (this.entries.Count >= MaximumEntries)
            this.entries.Clear();
        this.entries[installationId] = new Entry(account, now.Add(Lifetime));
        return account;
    }

    public ValueTask<IReadOnlyList<LinkedInstallation>> ListInstallationsAsync(
        AccountId accountId,
        CancellationToken cancellationToken) =>
        this.store.ListInstallationsAsync(accountId, cancellationToken);

    private sealed record Entry(LinkedLastFmAccount Account, DateTimeOffset ExpiresAt);
}
