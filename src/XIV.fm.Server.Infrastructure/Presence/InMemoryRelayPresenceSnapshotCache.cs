using System.Collections.Concurrent;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Infrastructure.Presence;

public sealed class InMemoryRelayPresenceSnapshotCache : IRelayPresenceSnapshotCache
{
    private readonly ConcurrentDictionary<CacheKey, PresenceSnapshot> snapshots = new();

    public ValueTask<PresenceSnapshot?> GetAsync(Guid relayId, long membershipRevision, DomainLocationScope location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(this.snapshots.TryGetValue(new CacheKey(relayId, membershipRevision, location), out var snapshot) ? snapshot : null);
    }

    public ValueTask SetAsync(Guid relayId, long membershipRevision, DomainLocationScope location, PresenceSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.snapshots[new CacheKey(relayId, membershipRevision, location)] = snapshot;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(Guid relayId, DomainLocationScope location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in this.snapshots.Keys.Where(key => key.RelayId == relayId && key.Location == location))
            this.snapshots.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveRelayAsync(Guid relayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in this.snapshots.Keys.Where(key => key.RelayId == relayId))
            this.snapshots.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    private sealed record CacheKey(Guid RelayId, long Revision, DomainLocationScope Location);
}
