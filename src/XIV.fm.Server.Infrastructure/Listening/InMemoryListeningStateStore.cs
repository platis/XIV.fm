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

    public ValueTask<IReadOnlyDictionary<AccountId, ListeningObservation>> GetManyAsync(
        IReadOnlyList<AccountId> accountIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observations = new Dictionary<AccountId, ListeningObservation>(accountIds.Count);
        foreach (var accountId in accountIds)
        {
            if (this.states.TryGetValue(accountId, out var observation))
                observations[accountId] = observation;
        }

        return ValueTask.FromResult<IReadOnlyDictionary<AccountId, ListeningObservation>>(observations);
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

    public ValueTask RemoveAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.states.TryRemove(accountId, out _);
        return ValueTask.CompletedTask;
    }
}
