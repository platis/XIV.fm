using XIV.fm.Contracts.V1;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Application.Abstractions;

public interface IRelayPresenceSnapshotCache
{
    ValueTask<PresenceSnapshot?> GetAsync(
        Guid relayId,
        long membershipRevision,
        DomainLocationScope location,
        CancellationToken cancellationToken);

    ValueTask SetAsync(
        Guid relayId,
        long membershipRevision,
        DomainLocationScope location,
        PresenceSnapshot snapshot,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(
        Guid relayId,
        DomainLocationScope location,
        CancellationToken cancellationToken);

    ValueTask RemoveRelayAsync(Guid relayId, CancellationToken cancellationToken);
}
