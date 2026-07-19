using XIV.fm.Server.Domain.Presence;

namespace XIV.fm.Server.Application.Abstractions;

public interface IPresenceStore
{
    ValueTask UpsertAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken);
}
