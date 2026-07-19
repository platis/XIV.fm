using System.Collections.Concurrent;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Domain.Presence;

namespace XIV.fm.Server.Infrastructure.Presence;

public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<InstallationId, PresenceHeartbeat> heartbeats = new();

    public ValueTask UpsertAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.heartbeats[heartbeat.InstallationId] = heartbeat;
        return ValueTask.CompletedTask;
    }

    public bool TryGet(InstallationId installationId, out PresenceHeartbeat? heartbeat) =>
        this.heartbeats.TryGetValue(installationId, out heartbeat);
}
