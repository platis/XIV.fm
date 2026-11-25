using Microsoft.EntityFrameworkCore;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.AccountLinks;
using XIV.fm.Server.Infrastructure.Accounts;
using XIV.fm.Server.Infrastructure.Persistence;
using XIV.fm.Server.Infrastructure.Relays;

namespace XIV.fm.Server.DurableTests;

[Collection(DurableStoresGroup.Name)]
public sealed class PostgresAccountDeletionStoreTests
{
    private readonly DurableStores stores;
    private readonly PostgresRelayStore relays;
    private readonly PostgresAccountDeletionStore deletions;

    public PostgresAccountDeletionStoreTests(DurableStores stores)
    {
        this.stores = stores;
        this.relays = new PostgresRelayStore(stores.Contexts);
        this.deletions = new PostgresAccountDeletionStore(stores.Contexts);
    }

    [Fact]
    public async Task DeletionRemovesEveryTraceAndNothingElse()
    {
        var (leaver, installation) = await this.LinkAsync();
        var friend = await this.stores.CreateAccountAsync();
        var owned = await this.CreateRelayAsync(leaver);
        var joined = await this.CreateRelayAsync(friend);
        await this.JoinAsync(leaver, owned, friend);
        await this.JoinAsync(friend, joined, leaver);
        var revision = (await this.relays.GetAsync(friend, joined, CancellationToken.None)).Value!.MembershipRevision;
        await using (var context = await this.stores.Contexts.CreateDbContextAsync())
        {
            context.RelayRemovals.Add(new RelayRemovalEntity
            {
                RelayId = joined,
                AccountId = leaver.Value,
                RemovedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        var deleted = await this.deletions.DeleteAsync(leaver, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(installation, Assert.Single(deleted.Installations).Value);
        Assert.Equal(owned, Assert.Single(deleted.DeletedRelayIds));
        Assert.Equal(joined, Assert.Single(deleted.LeftRelayIds));
        await using (var context = await this.stores.Contexts.CreateDbContextAsync())
        {
            Assert.False(await context.LastFmAccounts.AnyAsync(row => row.AccountId == leaver.Value));
            Assert.False(await context.InstallationCredentials.AnyAsync(row => row.AccountId == leaver.Value));
            Assert.False(await context.AccountLinkSessions.AnyAsync(row => row.AccountId == leaver.Value));
            Assert.False(await context.RelayMemberships.AnyAsync(row => row.AccountId == leaver.Value));
            Assert.False(await context.RelayRemovals.AnyAsync(row => row.AccountId == leaver.Value));
            Assert.False(await context.Relays.AnyAsync(row => row.RelayId == owned));
            Assert.False(await context.RelayInvitations.AnyAsync(row => row.RelayId == owned));
            var used = await context.RelayInvitations.SingleAsync(row => row.RelayId == joined);
            Assert.NotNull(used.AcceptedAt);
            Assert.Null(used.AcceptedByAccountId);
            Assert.True(await context.LastFmAccounts.AnyAsync(row => row.AccountId == friend.Value));
        }

        var remaining = Assert.Single(await this.relays.ListAsync(friend, CancellationToken.None));
        Assert.Equal(joined, remaining.RelayId);
        Assert.Equal(1, remaining.MemberCount);
        Assert.Equal(revision + 1, remaining.MembershipRevision);
    }

    [Fact]
    public async Task RepeatedAndSimultaneousDeletionsSucceed()
    {
        var (leaver, _) = await this.LinkAsync();
        await this.CreateRelayAsync(leaver);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            await this.deletions.DeleteAsync(leaver, DateTimeOffset.UtcNow, CancellationToken.None))));
        var repeated = await this.deletions.DeleteAsync(leaver, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(repeated.Installations);
        Assert.Empty(repeated.DeletedRelayIds);
    }

    [Fact]
    public async Task DeletionDuringJoinsLeavesNoMembership()
    {
        var friend = await this.stores.CreateAccountAsync();
        var joined = await this.CreateRelayAsync(friend);
        var (leaver, _) = await this.LinkAsync();
        var hashes = new[] { await this.InviteAsync(friend, joined), await this.InviteAsync(friend, joined) };

        await Task.WhenAll(
            Task.Run(async () => await this.deletions.DeleteAsync(leaver, DateTimeOffset.UtcNow, CancellationToken.None)),
            Task.Run(async () => await this.AcceptAsync(leaver, hashes[0])),
            Task.Run(async () => await this.AcceptAsync(leaver, hashes[1])));

        await using var context = await this.stores.Contexts.CreateDbContextAsync();
        Assert.False(await context.LastFmAccounts.AnyAsync(row => row.AccountId == leaver.Value));
        Assert.False(await context.RelayMemberships.AnyAsync(row => row.AccountId == leaver.Value));
    }

    private async Task<(AccountId Account, Guid Installation)> LinkAsync()
    {
        var links = new PostgresAccountLinkStore(this.stores.Contexts);
        var credentials = new PostgresInstallationCredentialStore(this.stores.Contexts, TimeProvider.System);
        var now = DateTimeOffset.UtcNow;
        var credential = CreateSecret();
        var state = CreateSecret();
        var session = new AccountLinkSessionId(Guid.NewGuid());
        await links.CreateAsync(
            new(session, credential, state, null, now, now.AddMinutes(10)),
            CancellationToken.None);
        Assert.True(await links.TryClaimAuthorizationAsync(session, state, CreateSecret(), now, CancellationToken.None));
        await links.CompleteAsync(
            session,
            new LastFmAccountIdentity($"listener-{Guid.NewGuid():N}"),
            now,
            CancellationToken.None);
        var installation = (await credentials.AuthenticateAsync(credential, CancellationToken.None))!.Value;
        var account = await links.GetForInstallationAsync(installation, CancellationToken.None);
        return (account!.AccountId, installation.Value);
    }

    private async Task<Guid> CreateRelayAsync(AccountId owner) => (await this.relays.CreateAsync(
        owner,
        "Deletion",
        "DELETION",
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        RelayOptions.Default,
        CancellationToken.None)).Value!.RelayId;

    private async Task<string> InviteAsync(AccountId owner, Guid relayId)
    {
        var hash = CreateSecret().ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        Assert.True((await this.relays.CreateInvitationAsync(
            owner,
            relayId,
            hash,
            now,
            now.AddDays(1),
            RelayOptions.Default,
            CancellationToken.None)).IsSuccess);
        return hash;
    }

    private async Task JoinAsync(AccountId owner, Guid relayId, AccountId member) =>
        Assert.True(await this.AcceptAsync(member, await this.InviteAsync(owner, relayId)));

    private async Task<bool> AcceptAsync(AccountId member, string hash) => (await this.relays.AcceptInvitationAsync(
        member,
        hash,
        DateTimeOffset.UtcNow,
        RelayOptions.Default,
        CancellationToken.None)).IsSuccess;

    private static string CreateSecret() => $"{Guid.NewGuid():N}{Guid.NewGuid():N}";
}
