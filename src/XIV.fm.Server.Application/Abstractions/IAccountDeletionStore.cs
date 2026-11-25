using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Application.Abstractions;

public interface IAccountDeletionStore
{
    /// <summary>
    /// Atomically removes the account, its installations and link sessions, every Relay it owns, and its
    /// membership in other Relays.
    /// </summary>
    ValueTask<DeletedAccount> DeleteAsync(
        AccountId accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record DeletedAccount(
    IReadOnlyList<InstallationId> Installations,
    IReadOnlyList<Guid> DeletedRelayIds,
    IReadOnlyList<Guid> LeftRelayIds);
