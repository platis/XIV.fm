using System.Collections.Concurrent;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Infrastructure.Presence;

public sealed class InMemoryPublicPresenceSnapshotCache : IPublicPresenceSnapshotCache
{
    private readonly ConcurrentDictionary<DomainLocationScope, PresenceSnapshot> snapshots = new();

    public ValueTask<PresenceSnapshot?> GetAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            this.snapshots.TryGetValue(location, out var snapshot) ? snapshot : null);
    }

    public ValueTask SetAsync(
        DomainLocationScope location,
        PresenceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.snapshots[location] = snapshot;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.snapshots.TryRemove(location, out _);
        return ValueTask.CompletedTask;
    }
}
