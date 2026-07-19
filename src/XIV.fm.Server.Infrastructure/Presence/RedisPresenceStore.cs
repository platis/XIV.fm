using System.Text.Json;
using StackExchange.Redis;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Presence;

namespace XIV.fm.Server.Infrastructure.Presence;

public sealed class RedisPresenceStore : IPresenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer connection;
    private readonly TimeProvider timeProvider;

    public RedisPresenceStore(IConnectionMultiplexer connection, TimeProvider timeProvider)
    {
        this.connection = connection;
        this.timeProvider = timeProvider;
    }

    public async ValueTask UpsertAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lifetime = heartbeat.ExpiresAt - this.timeProvider.GetUtcNow();
        if (lifetime <= TimeSpan.Zero)
            return;

        var value = new StoredPresence(
            heartbeat.InstallationId.Value,
            heartbeat.Character.Name,
            heartbeat.Character.HomeWorldId,
            heartbeat.Location.CurrentWorldId,
            heartbeat.Location.TerritoryId,
            heartbeat.Location.MapId,
            heartbeat.Location.InstanceId,
            heartbeat.Visibility.Mode,
            heartbeat.Visibility.RelayIds,
            heartbeat.SeenAt,
            heartbeat.ExpiresAt);
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        var database = this.connection.GetDatabase();
        await database.StringSetAsync(
            $"xivfm:presence:installation:{heartbeat.InstallationId}",
            payload,
            lifetime,
            When.Always,
            CommandFlags.None).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record StoredPresence(
        Guid InstallationId,
        string CharacterName,
        uint HomeWorldId,
        uint CurrentWorldId,
        uint TerritoryId,
        uint MapId,
        uint InstanceId,
        VisibilityMode Visibility,
        IReadOnlyList<Guid> RelayIds,
        DateTimeOffset SeenAt,
        DateTimeOffset ExpiresAt);
}
