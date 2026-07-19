using XIV.fm.Server.Application.Abstractions;

namespace XIV.fm.Server.Infrastructure.LastFm;

public sealed class LastFmRequestBudget : ILastFmRequestBudget, IDisposable
{
    private const int TokenLimit = 7;
    private const int QueueLimit = 20;

    private readonly SemaphoreSlim tokens = new(TokenLimit, TokenLimit);
    private readonly Timer replenishmentTimer;
    private int queuedRequests;
    private bool disposed;

    public LastFmRequestBudget()
    {
        this.replenishmentTimer = new Timer(
            _ => this.Replenish(),
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
    }

    public async ValueTask AcquireAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.tokens.Wait(0, cancellationToken))
            return;

        if (Interlocked.Increment(ref this.queuedRequests) > QueueLimit)
        {
            Interlocked.Decrement(ref this.queuedRequests);
            throw new LastFmAuthorizationException("The Last.fm request budget is currently full.");
        }

        try
        {
            await this.tokens.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref this.queuedRequests);
        }
    }

    public void Dispose()
    {
        this.disposed = true;
        this.replenishmentTimer.Dispose();
        this.tokens.Dispose();
    }

    private void Replenish()
    {
        if (this.disposed)
            return;

        var missing = TokenLimit - this.tokens.CurrentCount;
        if (missing <= 0)
            return;

        try
        {
            this.tokens.Release(missing);
        }
        catch (Exception exception) when (exception is SemaphoreFullException or ObjectDisposedException)
        {
            // A concurrent acquire/disposal changed the observed state; no token is lost.
        }
    }
}
