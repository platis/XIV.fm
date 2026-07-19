using System.Collections.Concurrent;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Infrastructure.Listening;

public sealed class InMemoryListeningPollLeaseStore : IListeningPollLeaseStore
{
    private readonly ConcurrentDictionary<AccountId, SemaphoreSlim> leases = new();

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(
        AccountId accountId,
        TimeSpan leaseLifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var semaphore = this.leases.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        return ValueTask.FromResult<IAsyncDisposable?>(
            semaphore.Wait(0, cancellationToken) ? new Lease(semaphore) : null);
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly SemaphoreSlim semaphore;
        private bool released;

        public Lease(SemaphoreSlim semaphore)
        {
            this.semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            if (!this.released)
            {
                this.released = true;
                this.semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
