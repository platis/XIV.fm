using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Application.Abstractions;

public interface IListeningStateStore
{
    ValueTask<ListeningObservation?> GetAsync(AccountId accountId, CancellationToken cancellationToken);

    ValueTask SetAsync(
        AccountId accountId,
        ListeningObservation observation,
        CancellationToken cancellationToken);
}
