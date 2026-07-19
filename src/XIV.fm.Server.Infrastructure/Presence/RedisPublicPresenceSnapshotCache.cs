using System.Text.Json;
using StackExchange.Redis;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Infrastructure.Presence;

public sealed class RedisPublicPresenceSnapshotCache : IPublicPresenceSnapshotCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer connection;
    private readonly TimeProvider timeProvider;

    public RedisPublicPresenceSnapshotCache(
        IConnectionMultiplexer connection,
        TimeProvider timeProvider)
    {
        this.connection = connection;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<PresenceSnapshot?> GetAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken)
    {
        var value = await this.connection.GetDatabase()
            .StringGetAsync(CreateKey(location))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<PresenceSnapshot>((string)value!, JsonOptions);
    }

    public async ValueTask SetAsync(
        DomainLocationScope location,
        PresenceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var lifetime = snapshot.ExpiresAt - this.timeProvider.GetUtcNow();
        if (lifetime <= TimeSpan.Zero)
            return;
        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
        await this.connection.GetDatabase()
            .StringSetAsync(CreateKey(location), payload, lifetime, When.Always, CommandFlags.None)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken)
    {
        await this.connection.GetDatabase()
            .KeyDeleteAsync(CreateKey(location))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static RedisKey CreateKey(DomainLocationScope location) =>
        $"xivfm:snapshot:public:{location.CurrentWorldId}:{location.TerritoryId}:{location.MapId}:{location.InstanceId}";
}
