using System.Text.Json;
using StackExchange.Redis;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
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

    public async ValueTask<PresenceHeartbeat?> UpsertAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lifetime = heartbeat.ExpiresAt - this.timeProvider.GetUtcNow();
        if (lifetime <= TimeSpan.Zero)
            return null;

        var stored = FromHeartbeat(heartbeat);
        var database = this.connection.GetDatabase();
        var installationValue = heartbeat.InstallationId.Value.ToString("D");
        var dataKey = CreateDataKey(installationValue);
        var pointerKey = CreatePointerKey(installationValue);
        var previousValues = await database.StringGetAsync([dataKey, pointerKey], CommandFlags.None).WaitAsync(cancellationToken).ConfigureAwait(false);
        var previous = previousValues[0].IsNullOrEmpty ? null : JsonSerializer.Deserialize<StoredPresence>((string)previousValues[0]!, JsonOptions);
        var previousIndexes = ParseIndexes(previousValues[1]);
        foreach (var previousIndex in previousIndexes)
            await database.SortedSetRemoveAsync(previousIndex, installationValue).WaitAsync(cancellationToken).ConfigureAwait(false);

        await database.StringSetAsync(dataKey, JsonSerializer.Serialize(stored, JsonOptions), lifetime).WaitAsync(cancellationToken).ConfigureAwait(false);
        var indexes = CreatePublicationIndexes(heartbeat);
        foreach (var index in indexes)
            await database.SortedSetAddAsync(index, installationValue, heartbeat.ExpiresAt.ToUnixTimeMilliseconds()).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (indexes.Length == 0)
            await database.KeyDeleteAsync(pointerKey).WaitAsync(cancellationToken).ConfigureAwait(false);
        else
            await database.StringSetAsync(pointerKey, JsonSerializer.Serialize(indexes.Select(key => (string)key!).ToArray(), JsonOptions), lifetime).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (heartbeat.AccountId is AccountId accountId)
        {
            var accountKey = CreateAccountInstallationsKey(accountId);
            await database.SetAddAsync(accountKey, installationValue).WaitAsync(cancellationToken).ConfigureAwait(false);
            await database.KeyExpireAsync(accountKey, TimeSpan.FromMinutes(2)).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return previous is null ? null : ToHeartbeat(previous);
    }

    public ValueTask<IReadOnlyList<PresenceHeartbeat>> GetPublicAsync(LocationScope location, DateTimeOffset now, int maximumResults, CancellationToken cancellationToken) =>
        this.GetIndexedAsync(CreatePublicIndexKey(location), location, now, maximumResults, VisibilityMode.Public, null, cancellationToken);

    public ValueTask<IReadOnlyList<PresenceHeartbeat>> GetRelayAsync(Guid relayId, LocationScope location, DateTimeOffset now, int maximumResults, CancellationToken cancellationToken) =>
        this.GetIndexedAsync(CreateRelayIndexKey(relayId, location), location, now, maximumResults, VisibilityMode.Custom, relayId, cancellationToken);

    public async ValueTask<IReadOnlySet<LocationScope>> RemoveRelayPublicationAsync(AccountId accountId, Guid relayId, CancellationToken cancellationToken)
    {
        var database = this.connection.GetDatabase();
        var members = await database.SetMembersAsync(CreateAccountInstallationsKey(accountId)).WaitAsync(cancellationToken).ConfigureAwait(false);
        var locations = new HashSet<LocationScope>();
        foreach (var member in members)
        {
            var installationValue = (string)member!;
            var dataKey = CreateDataKey(installationValue);
            var pointerKey = CreatePointerKey(installationValue);
            var values = await database.StringGetAsync([dataKey, pointerKey]).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (values[0].IsNullOrEmpty)
                continue;
            var stored = JsonSerializer.Deserialize<StoredPresence>((string)values[0]!, JsonOptions);
            if (stored?.AccountId != accountId.Value || stored.Visibility != VisibilityMode.Custom || !stored.RelayIds.Contains(relayId))
                continue;

            var location = new LocationScope(stored.CurrentWorldId, stored.TerritoryId, stored.MapId, stored.InstanceId);
            locations.Add(location);
            var removedIndex = CreateRelayIndexKey(relayId, location);
            await database.SortedSetRemoveAsync(removedIndex, installationValue).WaitAsync(cancellationToken).ConfigureAwait(false);
            var remaining = stored.RelayIds.Where(id => id != relayId).ToArray();
            var updated = stored with
            {
                Visibility = remaining.Length == 0 ? VisibilityMode.Private : VisibilityMode.Custom,
                RelayIds = remaining,
            };
            var lifetime = stored.ExpiresAt - this.timeProvider.GetUtcNow();
            if (lifetime <= TimeSpan.Zero)
                continue;
            await database.StringSetAsync(dataKey, JsonSerializer.Serialize(updated, JsonOptions), lifetime).WaitAsync(cancellationToken).ConfigureAwait(false);
            var pointerIndexes = ParseIndexes(values[1]).Where(key => key != removedIndex).ToArray();
            if (pointerIndexes.Length == 0)
                await database.KeyDeleteAsync(pointerKey).WaitAsync(cancellationToken).ConfigureAwait(false);
            else
                await database.StringSetAsync(pointerKey, JsonSerializer.Serialize(pointerIndexes.Select(key => (string)key!).ToArray(), JsonOptions), lifetime).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        return locations;
    }

    private async ValueTask<IReadOnlyList<PresenceHeartbeat>> GetIndexedAsync(
        RedisKey indexKey,
        LocationScope location,
        DateTimeOffset now,
        int maximumResults,
        VisibilityMode expectedMode,
        Guid? relayId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = this.connection.GetDatabase();
        await database.SortedSetRemoveRangeByScoreAsync(indexKey, double.NegativeInfinity, now.ToUnixTimeMilliseconds()).WaitAsync(cancellationToken).ConfigureAwait(false);
        var members = await database.SortedSetRangeByScoreAsync(indexKey, now.ToUnixTimeMilliseconds(), double.PositiveInfinity, Exclude.None, Order.Descending, 0, maximumResults).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (members.Length == 0)
            return [];
        var values = await database.StringGetAsync(members.Select(member => CreateDataKey((string)member!)).ToArray()).WaitAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<PresenceHeartbeat>(values.Length);
        foreach (var value in values)
        {
            if (value.IsNullOrEmpty)
                continue;
            var stored = JsonSerializer.Deserialize<StoredPresence>((string)value!, JsonOptions);
            if (stored?.AccountId is null || stored.ExpiresAt <= now || stored.Visibility != expectedMode ||
                (relayId is Guid requiredRelay && !stored.RelayIds.Contains(requiredRelay)))
                continue;
            var heartbeat = ToHeartbeat(stored);
            if (heartbeat.Location == location)
                result.Add(heartbeat);
        }
        return result;
    }

    private static StoredPresence FromHeartbeat(PresenceHeartbeat heartbeat) => new(
        heartbeat.InstallationId.Value,
        heartbeat.AccountId?.Value,
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

    private static PresenceHeartbeat ToHeartbeat(StoredPresence stored) => new(
        new InstallationId(stored.InstallationId),
        stored.AccountId is Guid accountId ? new AccountId(accountId) : null,
        new CharacterIdentity(stored.CharacterName, stored.HomeWorldId),
        new LocationScope(stored.CurrentWorldId, stored.TerritoryId, stored.MapId, stored.InstanceId),
        new VisibilitySelection(stored.Visibility, stored.RelayIds),
        stored.SeenAt,
        stored.ExpiresAt);

    private static RedisKey[] CreatePublicationIndexes(PresenceHeartbeat heartbeat)
    {
        if (heartbeat.AccountId is null)
            return [];
        if (heartbeat.Visibility.Mode == VisibilityMode.Public)
            return [CreatePublicIndexKey(heartbeat.Location)];
        if (heartbeat.Visibility.Mode == VisibilityMode.Custom)
            return heartbeat.Visibility.RelayIds.Select(relayId => CreateRelayIndexKey(relayId, heartbeat.Location)).ToArray();
        return [];
    }

    private static RedisKey[] ParseIndexes(RedisValue value)
    {
        if (value.IsNullOrEmpty)
            return [];
        var text = (string)value!;
        try
        {
            return (JsonSerializer.Deserialize<string[]>(text, JsonOptions) ?? []).Select(key => (RedisKey)key).ToArray();
        }
        catch (JsonException)
        {
            return [(RedisKey)text];
        }
    }

    private static RedisKey CreateDataKey(string installationId) => $"xivfm:presence:installation:{installationId}";
    private static RedisKey CreatePointerKey(string installationId) => $"xivfm:presence:publication:{installationId}";
    private static RedisKey CreateAccountInstallationsKey(AccountId accountId) => $"xivfm:presence:account:{accountId.Value:D}:installations";
    private static RedisKey CreatePublicIndexKey(LocationScope location) => $"xivfm:presence:public:{LocationKey(location)}";
    private static RedisKey CreateRelayIndexKey(Guid relayId, LocationScope location) => $"xivfm:presence:relay:{relayId:D}:{LocationKey(location)}";
    private static string LocationKey(LocationScope location) => $"{location.CurrentWorldId}:{location.TerritoryId}:{location.MapId}:{location.InstanceId}";

    private sealed record StoredPresence(
        Guid InstallationId,
        Guid? AccountId,
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
