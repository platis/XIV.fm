using XIV.fm.Contracts.V1;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Application.Abstractions;

public interface IPublicPresenceSnapshotCache
{
    ValueTask<PresenceSnapshot?> GetAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken);

    ValueTask SetAsync(
        DomainLocationScope location,
        PresenceSnapshot snapshot,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken);
}
