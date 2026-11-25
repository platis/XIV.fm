using Microsoft.EntityFrameworkCore;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Retention;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.Infrastructure.Retention;

public sealed class PostgresRetentionStore : IRetentionStore
{
    private readonly IDbContextFactory<XivFmDbContext> contextFactory;

    public PostgresRetentionStore(IDbContextFactory<XivFmDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async ValueTask<RetentionResult> RemoveExpiredAsync(
        DateTimeOffset now,
        RetentionPolicy policy,
        CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sessionsBefore = now.Subtract(policy.ExpiredLinkSessions);
        var sessions = await context.AccountLinkSessions
            .Where(session => session.ExpiresAt < sessionsBefore)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var revokedBefore = now.Subtract(policy.RevokedInstallations);
        var installations = await context.InstallationCredentials
            .Where(installation => installation.RevokedAt != null && installation.RevokedAt < revokedBefore)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var invitationsBefore = now.Subtract(policy.EndedInvitations);
        var invitations = await context.RelayInvitations
            .Where(invitation =>
                (invitation.AcceptedAt ?? invitation.RevokedAt ?? invitation.ExpiresAt) < invitationsBefore)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // The database cascades to the invitations and removal restrictions of a deleted Relay.
        var relaysBefore = now.Subtract(policy.DeletedRelays);
        var relays = await context.Relays
            .Where(relay => relay.DeletedAt != null && relay.DeletedAt < relaysBefore)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RetentionResult(sessions, installations, invitations, relays);
    }
}
