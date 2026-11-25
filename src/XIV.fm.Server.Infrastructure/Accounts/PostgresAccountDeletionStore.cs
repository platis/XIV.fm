using Microsoft.EntityFrameworkCore;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.Infrastructure.Accounts;

public sealed class PostgresAccountDeletionStore : IAccountDeletionStore
{
    private readonly IDbContextFactory<XivFmDbContext> contextFactory;

    public PostgresAccountDeletionStore(IDbContextFactory<XivFmDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async ValueTask<DeletedAccount> DeleteAsync(
        AccountId accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await SerializableTransaction.ExecuteAsync(
            this.contextFactory,
            async (context, token) => (await DeleteAsync(context, accountId.Value, now, token).ConfigureAwait(false), Commit: true),
            cancellationToken).ConfigureAwait(false);

    private static async Task<DeletedAccount> DeleteAsync(
        XivFmDbContext context,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var installations = await context.InstallationCredentials
            .Where(installation => installation.AccountId == accountId)
            .Select(installation => installation.InstallationId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var ownedRelays = await context.Relays
            .Where(relay => relay.OwnerAccountId == accountId)
            .Select(relay => relay.RelayId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var leftRelays = await context.RelayMemberships
            .Where(member => member.AccountId == accountId && member.Relay.OwnerAccountId != accountId)
            .Select(member => member.RelayId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        // The database cascades to the members, invitations, and removal restrictions of each owned Relay.
        await context.Relays
            .Where(relay => relay.OwnerAccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await context.Relays
            .Where(relay => leftRelays.Contains(relay.RelayId))
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(relay => relay.MembershipRevision, relay => relay.MembershipRevision + 1)
                    .SetProperty(relay => relay.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
        await context.RelayMemberships
            .Where(member => member.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await context.RelayRemovals
            .Where(removal => removal.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Another owner's invitation stays used, without recording who used it.
        await context.RelayInvitations
            .Where(invitation => invitation.AcceptedByAccountId == accountId)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(invitation => invitation.AcceptedByAccountId, (Guid?)null),
                cancellationToken)
            .ConfigureAwait(false);
        await context.AccountLinkSessions
            .Where(session => session.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await context.InstallationCredentials
            .Where(installation => installation.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await context.LastFmAccounts
            .Where(account => account.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DeletedAccount(
            installations.Select(installation => new InstallationId(installation)).ToArray(),
            ownedRelays,
            leftRelays);
    }
}
