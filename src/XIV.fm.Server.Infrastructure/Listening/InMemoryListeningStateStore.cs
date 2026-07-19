using System.Collections.Concurrent;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Infrastructure.Listening;

public sealed class InMemoryListeningStateStore : IListeningStateStore
{
    private readonly ConcurrentDictionary<AccountId, ListeningObservation> states = new();

    public ValueTask<ListeningObservation?> GetAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            this.states.TryGetValue(accountId, out var observation) ? observation : null);
    }

    public ValueTask SetAsync(
        AccountId accountId,
        ListeningObservation observation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.states[accountId] = observation;
        return ValueTask.CompletedTask;
    }
}
