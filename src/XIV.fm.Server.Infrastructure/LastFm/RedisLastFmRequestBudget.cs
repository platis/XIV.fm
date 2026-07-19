using StackExchange.Redis;
using XIV.fm.Server.Application.Abstractions;

namespace XIV.fm.Server.Infrastructure.LastFm;

public sealed class RedisLastFmRequestBudget : ILastFmRequestBudget
{
    private const int QueueLimit = 20;
    private const string BudgetScript = """
        local current = redis.call('TIME')
        local now = (tonumber(current[1]) * 1000) + math.floor(tonumber(current[2]) / 1000)
        local values = redis.call('HMGET', KEYS[1], 'tokens', 'updated_at')
        local tokens = tonumber(values[1]) or tonumber(ARGV[1])
        local updated_at = tonumber(values[2]) or now
        tokens = math.min(tonumber(ARGV[1]), tokens + ((now - updated_at) * tonumber(ARGV[2])))
        local acquired = 0
        local retry_after = 0
        if tokens >= 1 then
          tokens = tokens - 1
          acquired = 1
        else
          retry_after = math.ceil((1 - tokens) / tonumber(ARGV[2]))
        end
        redis.call('HSET', KEYS[1], 'tokens', tokens, 'updated_at', now)
        redis.call('PEXPIRE', KEYS[1], 10000)
        return { acquired, retry_after }
        """;

    private readonly IConnectionMultiplexer connection;
    private int queuedRequests;

    public RedisLastFmRequestBudget(IConnectionMultiplexer connection)
    {
        this.connection = connection;
    }

    public async ValueTask AcquireAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref this.queuedRequests) > QueueLimit)
        {
            Interlocked.Decrement(ref this.queuedRequests);
            throw new LastFmAuthorizationException("The Last.fm request budget is currently full.");
        }

        try
        {
            var database = this.connection.GetDatabase();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await database.ScriptEvaluateAsync(
                    BudgetScript,
                    ["xivfm:lastfm:global-request-budget"],
                    [7, 0.0035],
                    CommandFlags.None).WaitAsync(cancellationToken).ConfigureAwait(false);
                var values = (RedisResult[])result!;
                if ((long)values[0] == 1)
                    return;

                var retryAfterMilliseconds = Math.Clamp((long)values[1], 1, 2000);
                await Task.Delay(
                    TimeSpan.FromMilliseconds(retryAfterMilliseconds),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Decrement(ref this.queuedRequests);
        }
    }
}
