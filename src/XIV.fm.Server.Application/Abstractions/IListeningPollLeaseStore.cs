using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Application.Abstractions;

public interface IListeningPollLeaseStore
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(
        AccountId accountId,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken);
}
