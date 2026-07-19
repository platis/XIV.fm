using System.Collections.Concurrent;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Domain.Presence;

namespace XIV.fm.Server.Infrastructure.Presence;

public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<InstallationId, PresenceHeartbeat> heartbeats = new();

    public ValueTask<PresenceHeartbeat?> UpsertAsync(
        PresenceHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.heartbeats.TryGetValue(heartbeat.InstallationId, out var previous);
        this.heartbeats[heartbeat.InstallationId] = heartbeat;
        return ValueTask.FromResult<PresenceHeartbeat?>(previous);
    }

    public ValueTask<IReadOnlyList<PresenceHeartbeat>> GetPublicAsync(
        LocationScope location,
        DateTimeOffset now,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PresenceHeartbeat> result = this.heartbeats.Values
            .Where(heartbeat =>
                heartbeat.AccountId is not null &&
                heartbeat.Visibility.Mode == VisibilityMode.Public &&
                heartbeat.ExpiresAt > now &&
                heartbeat.Location == location)
            .OrderByDescending(heartbeat => heartbeat.SeenAt)
            .Take(maximumResults)
            .ToArray();
        return ValueTask.FromResult(result);
    }

    public ValueTask<IReadOnlyList<PresenceHeartbeat>> GetRelayAsync(
        Guid relayId,
        LocationScope location,
        DateTimeOffset now,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PresenceHeartbeat> result = this.heartbeats.Values
            .Where(heartbeat =>
                heartbeat.AccountId is not null &&
                heartbeat.Visibility.Mode == VisibilityMode.Custom &&
                heartbeat.Visibility.RelayIds.Contains(relayId) &&
                heartbeat.ExpiresAt > now &&
                heartbeat.Location == location)
            .OrderByDescending(heartbeat => heartbeat.SeenAt)
            .Take(maximumResults)
            .ToArray();
        return ValueTask.FromResult(result);
    }

    public ValueTask<IReadOnlySet<LocationScope>> RemoveRelayPublicationAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locations = new HashSet<LocationScope>();
        foreach (var pair in this.heartbeats.ToArray())
        {
            var heartbeat = pair.Value;
            if (heartbeat.AccountId != accountId || heartbeat.Visibility.Mode != VisibilityMode.Custom ||
                !heartbeat.Visibility.RelayIds.Contains(relayId))
                continue;
            locations.Add(heartbeat.Location);
            var remaining = heartbeat.Visibility.RelayIds.Where(id => id != relayId).ToArray();
            this.heartbeats[pair.Key] = heartbeat with
            {
                Visibility = remaining.Length == 0
                    ? new VisibilitySelection(VisibilityMode.Private, [])
                    : new VisibilitySelection(VisibilityMode.Custom, remaining),
            };
        }
        return ValueTask.FromResult<IReadOnlySet<LocationScope>>(locations);
    }

    public bool TryGet(InstallationId installationId, out PresenceHeartbeat? heartbeat) =>
        this.heartbeats.TryGetValue(installationId, out heartbeat);
}
