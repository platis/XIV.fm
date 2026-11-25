using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.AccountLinks;
using XIV.fm.Server.Infrastructure.Relays;

namespace XIV.fm.Server.Infrastructure.Accounts;

public sealed class InMemoryAccountDeletionStore : IAccountDeletionStore
{
    private readonly InMemoryAccountLinkStore accounts;
    private readonly InMemoryRelayStore relays;

    public InMemoryAccountDeletionStore(InMemoryAccountLinkStore accounts, InMemoryRelayStore relays)
    {
        this.accounts = accounts;
        this.relays = relays;
    }

    public async ValueTask<DeletedAccount> DeleteAsync(
        AccountId accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (deletedRelays, leftRelays) = this.relays.RemoveAccount(accountId, now);
        var installations = await this.accounts
            .RemoveAccountAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        return new DeletedAccount(installations, deletedRelays, leftRelays);
    }
}
