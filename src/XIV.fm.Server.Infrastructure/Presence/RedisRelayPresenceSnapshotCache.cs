using System.Text.Json;
using StackExchange.Redis;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Infrastructure.Presence;

public sealed class RedisRelayPresenceSnapshotCache : IRelayPresenceSnapshotCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer connection;
    private readonly TimeProvider timeProvider;

    public RedisRelayPresenceSnapshotCache(IConnectionMultiplexer connection, TimeProvider timeProvider)
    {
        this.connection = connection;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<PresenceSnapshot?> GetAsync(Guid relayId, long membershipRevision, DomainLocationScope location, CancellationToken cancellationToken)
    {
        var value = await this.connection.GetDatabase().StringGetAsync(CreateKey(relayId, membershipRevision, location)).WaitAsync(cancellationToken).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<PresenceSnapshot>((string)value!, JsonOptions);
    }

    public async ValueTask SetAsync(Guid relayId, long membershipRevision, DomainLocationScope location, PresenceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var lifetime = snapshot.ExpiresAt - this.timeProvider.GetUtcNow();
        if (lifetime <= TimeSpan.Zero)
            return;
        var database = this.connection.GetDatabase();
        var key = CreateKey(relayId, membershipRevision, location);
        var scopeSet = CreateScopeSet(relayId);
        await database.StringSetAsync(key, JsonSerializer.Serialize(snapshot, JsonOptions), lifetime).WaitAsync(cancellationToken).ConfigureAwait(false);
        await database.SetAddAsync(scopeSet, (string)key!).WaitAsync(cancellationToken).ConfigureAwait(false);
        await database.KeyExpireAsync(scopeSet, TimeSpan.FromMinutes(2)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(Guid relayId, DomainLocationScope location, CancellationToken cancellationToken)
    {
        var database = this.connection.GetDatabase();
        var scopeSet = CreateScopeSet(relayId);
        var keys = await database.SetMembersAsync(scopeSet).WaitAsync(cancellationToken).ConfigureAwait(false);
        var matching = keys.Where(value => ((string)value!).EndsWith(CreateLocationSuffix(location), StringComparison.Ordinal)).Select(value => (RedisKey)(string)value!).ToArray();
        if (matching.Length != 0)
            await database.KeyDeleteAsync(matching).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RemoveRelayAsync(Guid relayId, CancellationToken cancellationToken)
    {
        var database = this.connection.GetDatabase();
        var scopeSet = CreateScopeSet(relayId);
        var keys = (await database.SetMembersAsync(scopeSet).WaitAsync(cancellationToken).ConfigureAwait(false)).Select(value => (RedisKey)(string)value!).Append(scopeSet).ToArray();
        if (keys.Length != 0)
            await database.KeyDeleteAsync(keys).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static RedisKey CreateKey(Guid relayId, long revision, DomainLocationScope location) =>
        $"xivfm:snapshot:relay:{relayId:D}:{revision}:{CreateLocationSuffix(location)}";

    private static RedisKey CreateScopeSet(Guid relayId) => $"xivfm:snapshot:relay-keys:{relayId:D}";

    private static string CreateLocationSuffix(DomainLocationScope location) =>
        $"{location.CurrentWorldId}:{location.TerritoryId}:{location.MapId}:{location.InstanceId}";
}
