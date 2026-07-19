using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Presence;

namespace XIV.fm.Server.Application.Abstractions;

public interface IPresenceStore
{
    ValueTask<PresenceHeartbeat?> UpsertAsync(
        PresenceHeartbeat heartbeat,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<PresenceHeartbeat>> GetPublicAsync(
        LocationScope location,
        DateTimeOffset now,
        int maximumResults,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<PresenceHeartbeat>> GetRelayAsync(
        Guid relayId,
        LocationScope location,
        DateTimeOffset now,
        int maximumResults,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlySet<LocationScope>> RemoveRelayPublicationAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken);
}
