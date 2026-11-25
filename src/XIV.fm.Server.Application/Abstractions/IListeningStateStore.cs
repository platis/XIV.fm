using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Application.Abstractions;

public interface IListeningStateStore
{
    ValueTask<ListeningObservation?> GetAsync(AccountId accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads several accounts at once. Accounts without a cached observation are absent from the result.
    /// </summary>
    ValueTask<IReadOnlyDictionary<AccountId, ListeningObservation>> GetManyAsync(
        IReadOnlyList<AccountId> accountIds,
        CancellationToken cancellationToken);

    ValueTask SetAsync(
        AccountId accountId,
        ListeningObservation observation,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(AccountId accountId, CancellationToken cancellationToken);
}
