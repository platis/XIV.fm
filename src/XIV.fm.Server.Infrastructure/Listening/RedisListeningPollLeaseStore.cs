using StackExchange.Redis;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Infrastructure.Listening;

public sealed class RedisListeningPollLeaseStore : IListeningPollLeaseStore
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private readonly IConnectionMultiplexer connection;

    public RedisListeningPollLeaseStore(IConnectionMultiplexer connection)
    {
        this.connection = connection;
    }

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        AccountId accountId,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = this.connection.GetDatabase();
        var key = (RedisKey)$"xivfm:listening:poll-lease:{accountId.Value:D}";
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var acquired = await database.StringSetAsync(
            key,
            token,
            leaseLifetime,
            When.NotExists,
            CommandFlags.None).WaitAsync(cancellationToken).ConfigureAwait(false);
        return acquired ? new Lease(database, key, token) : null;
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly IDatabase database;
        private readonly RedisKey key;
        private readonly RedisValue token;

        public Lease(IDatabase database, RedisKey key, RedisValue token)
        {
            this.database = database;
            this.key = key;
            this.token = token;
        }

        public async ValueTask DisposeAsync()
        {
            await this.database.ScriptEvaluateAsync(
                ReleaseScript,
                [this.key],
                [this.token],
                CommandFlags.FireAndForget).ConfigureAwait(false);
        }
    }
}
