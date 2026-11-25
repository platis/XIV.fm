using Microsoft.EntityFrameworkCore;
using XIV.fm.Server.Application.Retention;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.Persistence;
using XIV.fm.Server.Infrastructure.Retention;

namespace XIV.fm.Server.DurableTests;

[Collection(DurableStoresGroup.Name)]
public sealed class PostgresRetentionStoreTests
{
    private readonly DurableStores stores;

    public PostgresRetentionStoreTests(DurableStores stores)
    {
        this.stores = stores;
    }

    [Fact]
    public async Task OnlyRecordsPastTheirRetentionAreRemoved()
    {
        // Far in the past, so records created by other tests are never eligible.
        var now = DateTimeOffset.UtcNow.AddYears(-5);
        var owner = await this.stores.CreateAccountAsync();
        var oldSession = CreateSession(now.AddHours(-25));
        var recentSession = CreateSession(now.AddHours(-23));
        var oldRevoked = CreateInstallation(owner, now.AddDays(-31));
        var recentRevoked = CreateInstallation(owner, now.AddDays(-29));
        var active = CreateInstallation(owner, null);
        var oldDeleted = CreateRelay(owner, now.AddDays(-61));
        var recentDeleted = CreateRelay(owner, now.AddDays(-59));
        var live = CreateRelay(owner, null);
        var oldAccepted = CreateInvitation(live, now.AddDays(-40), accepted: now.AddDays(-31));
        var recentAccepted = CreateInvitation(live, now.AddDays(-40), accepted: now.AddDays(-29));
        var oldRevokedInvitation = CreateInvitation(live, now.AddDays(-20), revoked: now.AddDays(-31));
        var oldExpired = CreateInvitation(live, now.AddDays(-31));
        var open = CreateInvitation(live, now.AddDays(3));
        var removal = new RelayRemovalEntity
        {
            RelayId = live.RelayId,
            AccountId = owner.Value,
            RemovedAt = now.AddDays(-400),
        };
        await using (var context = await this.stores.Contexts.CreateDbContextAsync())
        {
            context.AddRange(oldSession, recentSession, oldRevoked, recentRevoked, active);
            context.AddRange(oldDeleted, recentDeleted, live, removal);
            context.AddRange(oldAccepted, recentAccepted, oldRevokedInvitation, oldExpired, open);
            context.Add(CreateInvitation(oldDeleted, now.AddDays(3)));
            await context.SaveChangesAsync();
        }

        var removed = await new PostgresRetentionStore(this.stores.Contexts).RemoveExpiredAsync(
            now,
            RetentionPolicy.Default,
            CancellationToken.None);

        Assert.Equal(new RetentionResult(1, 1, 3, 1), removed);
        await using (var context = await this.stores.Contexts.CreateDbContextAsync())
        {
            Assert.False(await context.AccountLinkSessions.AnyAsync(row => row.SessionId == oldSession.SessionId));
            Assert.True(await context.AccountLinkSessions.AnyAsync(row => row.SessionId == recentSession.SessionId));
            Assert.Equal(
                new[] { recentRevoked.InstallationId, active.InstallationId }.Order(),
                (await context.InstallationCredentials.Where(row => row.AccountId == owner.Value)
                    .Select(row => row.InstallationId).ToArrayAsync()).Order());
            Assert.Equal(
                new[] { recentDeleted.RelayId, live.RelayId }.Order(),
                (await context.Relays.Where(row => row.OwnerAccountId == owner.Value)
                    .Select(row => row.RelayId).ToArrayAsync()).Order());
            Assert.Equal(
                new[] { recentAccepted.InvitationId, open.InvitationId }.Order(),
                (await context.RelayInvitations.Where(row => row.RelayId == live.RelayId)
                    .Select(row => row.InvitationId).ToArrayAsync()).Order());
            Assert.True(await context.RelayRemovals.AnyAsync(row => row.RelayId == live.RelayId));
            Assert.True(await context.LastFmAccounts.AnyAsync(row => row.AccountId == owner.Value));
        }

        var repeated = await new PostgresRetentionStore(this.stores.Contexts).RemoveExpiredAsync(
            now,
            RetentionPolicy.Default,
            CancellationToken.None);
        Assert.Equal(0, repeated.Total);
    }

    [Fact]
    public void DeletedRelaysOutliveTheCreationLimitWindow()
    {
        Assert.Equal(
            TimeSpan.FromDays(60),
            RetentionPolicy.Default.Covering(TimeSpan.FromDays(30)).DeletedRelays);
        Assert.Equal(
            TimeSpan.FromDays(91),
            RetentionPolicy.Default.Covering(TimeSpan.FromDays(90)).DeletedRelays);
    }

    private static string CreateHash() => $"{Guid.NewGuid():N}{Guid.NewGuid():N}".ToUpperInvariant();

    private static AccountLinkSessionEntity CreateSession(DateTimeOffset expiresAt) => new()
    {
        SessionId = Guid.NewGuid(),
        LinkCredentialHash = CreateHash(),
        CallbackStateHash = CreateHash(),
        CreatedAt = expiresAt.AddMinutes(-10),
        ExpiresAt = expiresAt,
    };

    private static InstallationCredentialEntity CreateInstallation(AccountId account, DateTimeOffset? revokedAt) => new()
    {
        InstallationId = Guid.NewGuid(),
        AccountId = account.Value,
        CredentialHash = CreateHash(),
        CreatedAt = DateTimeOffset.UtcNow.AddYears(-6),
        RevokedAt = revokedAt,
    };

    private static RelayEntity CreateRelay(AccountId owner, DateTimeOffset? deletedAt) => new()
    {
        RelayId = Guid.NewGuid(),
        Name = "Retention",
        NormalizedName = "RETENTION",
        OwnerAccountId = owner.Value,
        IdempotencyKey = Guid.NewGuid(),
        MembershipRevision = 1,
        CreatedAt = DateTimeOffset.UtcNow.AddYears(-6),
        UpdatedAt = DateTimeOffset.UtcNow.AddYears(-6),
        DeletedAt = deletedAt,
    };

    private static RelayInvitationEntity CreateInvitation(
        RelayEntity relay,
        DateTimeOffset expiresAt,
        DateTimeOffset? accepted = null,
        DateTimeOffset? revoked = null) => new()
        {
            InvitationId = Guid.NewGuid(),
            RelayId = relay.RelayId,
            TokenHash = CreateHash(),
            CreatedAt = expiresAt.AddDays(-7),
            ExpiresAt = expiresAt,
            AcceptedAt = accepted,
            RevokedAt = revoked,
        };
}
