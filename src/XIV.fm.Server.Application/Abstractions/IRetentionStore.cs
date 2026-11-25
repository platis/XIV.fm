using XIV.fm.Server.Application.Retention;

namespace XIV.fm.Server.Application.Abstractions;

public interface IRetentionStore
{
    ValueTask<RetentionResult> RemoveExpiredAsync(
        DateTimeOffset now,
        RetentionPolicy policy,
        CancellationToken cancellationToken);
}
