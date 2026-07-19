using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.Infrastructure.AccountLinks;

public sealed class PostgresAccountLinkStore : IAccountLinkStore, ILinkedAccountResolver
{
    private readonly IDbContextFactory<XivFmDbContext> contextFactory;

    public PostgresAccountLinkStore(IDbContextFactory<XivFmDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async ValueTask CreateAsync(NewAccountLinkSession session, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.AccountLinkSessions.Add(new AccountLinkSessionEntity
        {
            SessionId = session.SessionId.Value,
            LinkCredentialHash = SecretHash.Compute(session.LinkCredential),
            CallbackStateHash = SecretHash.Compute(session.CallbackState),
            ProviderTokenHash = session.ProviderToken is null
                ? null
                : SecretHash.Compute(session.ProviderToken),
            Status = (int)StoredAccountLinkStatus.Pending,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryClaimAuthorizationAsync(
        AccountLinkSessionId sessionId,
        string callbackState,
        string providerToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var providerTokenHash = SecretHash.Compute(providerToken);
        try
        {
            var changed = await context.AccountLinkSessions
                .Where(entity =>
                    entity.SessionId == sessionId.Value &&
                    entity.Status == (int)StoredAccountLinkStatus.Pending &&
                    entity.ExpiresAt > now &&
                    entity.CallbackStateHash == SecretHash.Compute(callbackState) &&
                    (entity.ProviderTokenHash == null || entity.ProviderTokenHash == providerTokenHash))
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(entity => entity.ProviderTokenHash, providerTokenHash)
                        .SetProperty(entity => entity.Status, (int)StoredAccountLinkStatus.Authorizing)
                        .SetProperty(entity => entity.AuthorizationStartedAt, now),
                    cancellationToken)
                .ConfigureAwait(false);
            return changed == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public async ValueTask CompleteAsync(
        AccountLinkSessionId sessionId,
        LastFmAccountIdentity identity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        var session = await context.AccountLinkSessions
            .SingleOrDefaultAsync(entity => entity.SessionId == sessionId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (session is null ||
            session.Status != (int)StoredAccountLinkStatus.Authorizing ||
            session.ExpiresAt <= now)
        {
            throw new InvalidOperationException("The account-link session cannot be completed.");
        }

        var account = await context.LastFmAccounts
            .SingleOrDefaultAsync(
                entity => entity.NormalizedName == identity.NormalizedName,
                cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            account = new LastFmAccountEntity
            {
                AccountId = Guid.NewGuid(),
                CanonicalName = identity.CanonicalName,
                NormalizedName = identity.NormalizedName,
                CreatedAt = now,
            };
            context.LastFmAccounts.Add(account);
        }
        else
        {
            account.CanonicalName = identity.CanonicalName;
        }

        context.InstallationCredentials.Add(new InstallationCredentialEntity
        {
            InstallationId = Guid.NewGuid(),
            AccountId = account.AccountId,
            CredentialHash = session.LinkCredentialHash,
            CreatedAt = now,
        });
        session.Account = account;
        session.Status = (int)StoredAccountLinkStatus.Linked;
        session.CompletedAt = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask FailAsync(
        AccountLinkSessionId sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.AccountLinkSessions
            .Where(entity =>
                entity.SessionId == sessionId.Value &&
                entity.Status == (int)StoredAccountLinkStatus.Authorizing)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(entity => entity.Status, (int)StoredAccountLinkStatus.Failed)
                    .SetProperty(entity => entity.CompletedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<LinkedLastFmAccount?> GetForInstallationAsync(
        InstallationId installationId,
        CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var account = await context.InstallationCredentials
            .AsNoTracking()
            .Where(entity =>
                entity.InstallationId == installationId.Value &&
                entity.RevokedAt == null &&
                entity.Account != null)
            .Select(entity => new
            {
                entity.AccountId,
                entity.Account!.CanonicalName,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return account?.AccountId is Guid accountId
            ? new LinkedLastFmAccount(
                new AccountId(accountId),
                new LastFmAccountIdentity(account.CanonicalName))
            : null;
    }

    public async ValueTask<StoredAccountLink?> GetAsync(
        AccountLinkSessionId sessionId,
        string linkCredential,
        CancellationToken cancellationToken)
    {
        var hash = SecretHash.Compute(linkCredential);
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.AccountLinkSessions
            .AsNoTracking()
            .Where(entity => entity.SessionId == sessionId.Value && entity.LinkCredentialHash == hash)
            .Select(entity => new StoredAccountLink(
                (StoredAccountLinkStatus)entity.Status,
                entity.ExpiresAt,
                entity.Account == null ? null : entity.Account.CanonicalName))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
