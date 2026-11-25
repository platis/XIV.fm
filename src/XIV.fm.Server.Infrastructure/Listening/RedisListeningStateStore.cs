using System.Text.Json;
using StackExchange.Redis;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Infrastructure.Listening;

public sealed class RedisListeningStateStore : IListeningStateStore
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer connection;

    public RedisListeningStateStore(IConnectionMultiplexer connection)
    {
        this.connection = connection;
    }

    public async ValueTask<ListeningObservation?> GetAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await this.connection.GetDatabase()
            .StringGetAsync(CreateKey(accountId))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<ListeningObservation>((string)value!, JsonOptions);
    }

    public async ValueTask<IReadOnlyDictionary<AccountId, ListeningObservation>> GetManyAsync(
        IReadOnlyList<AccountId> accountIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observations = new Dictionary<AccountId, ListeningObservation>(accountIds.Count);
        if (accountIds.Count == 0)
            return observations;

        var values = await this.connection.GetDatabase()
            .StringGetAsync(accountIds.Select(CreateKey).ToArray())
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < values.Length; index++)
        {
            if (!values[index].IsNullOrEmpty &&
                JsonSerializer.Deserialize<ListeningObservation>((string)values[index]!, JsonOptions) is { } observation)
            {
                observations[accountIds[index]] = observation;
            }
        }

        return observations;
    }

    public async ValueTask SetAsync(
        AccountId accountId,
        ListeningObservation observation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.Serialize(observation, JsonOptions);
        await this.connection.GetDatabase()
            .StringSetAsync(CreateKey(accountId), payload, CacheLifetime, When.Always, CommandFlags.None)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(AccountId accountId, CancellationToken cancellationToken) =>
        await this.connection.GetDatabase()
            .KeyDeleteAsync(CreateKey(accountId))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

    private static RedisKey CreateKey(AccountId accountId) =>
        $"xivfm:listening:account:{accountId.Value:D}";
}
