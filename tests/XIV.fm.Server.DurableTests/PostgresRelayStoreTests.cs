using Microsoft.EntityFrameworkCore;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.Relays;

namespace XIV.fm.Server.DurableTests;

[Collection(DurableStoresGroup.Name)]
public sealed class PostgresRelayStoreTests
{
    private readonly DurableStores stores;
    private readonly PostgresRelayStore relays;

    public PostgresRelayStoreTests(DurableStores stores)
    {
        this.stores = stores;
        this.relays = new PostgresRelayStore(stores.Contexts);
    }

    [Fact]
    public async Task MigrationsMatchTheModel()
    {
        await using var context = await this.stores.Contexts.CreateDbContextAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task SimultaneousJoinsAllSucceed()
    {
        var (owner, relay) = await this.CreateRelayAsync();
        var joins = await this.PrepareJoinsAsync(owner, relay.RelayId, 10);

        var results = await Task.WhenAll(joins.Select(join => this.JoinAsync(join, RelayOptions.Default)));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Failure?.ToString()));
        var stored = await this.relays.GetAsync(owner, relay.RelayId, CancellationToken.None);
        Assert.Equal(11, stored.Value!.MemberCount);
        Assert.Equal(relay.MembershipRevision + 10, stored.Value.MembershipRevision);
    }

    [Fact]
    public async Task SimultaneousJoinsCannotExceedTheMemberLimit()
    {
        var (owner, relay) = await this.CreateRelayAsync();
        var joins = await this.PrepareJoinsAsync(owner, relay.RelayId, 8);
        var limits = RelayOptions.Default with { MaximumMembersPerRelay = 4 };

        var results = await Task.WhenAll(joins.Select(join => this.JoinAsync(join, limits)));

        Assert.Equal(3, results.Count(result => result.IsSuccess));
        Assert.All(
            results.Where(result => !result.IsSuccess),
            result => Assert.Equal(RelayStoreFailure.MemberLimit, result.Failure));
        Assert.Equal(
            4,
            (await this.relays.GetAsync(owner, relay.RelayId, CancellationToken.None)).Value!.MemberCount);
    }

    [Fact]
    public async Task OneInvitationAdmitsOnlyOneOfManySimultaneousAccounts()
    {
        var (owner, relay) = await this.CreateRelayAsync();
        var hash = await this.InviteAsync(owner, relay.RelayId);
        var accounts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => this.stores.CreateAccountAsync()));

        var results = await Task.WhenAll(accounts.Select(account =>
            this.JoinAsync((account, hash), RelayOptions.Default)));

        Assert.Single(results, result => result.IsSuccess);
        Assert.All(
            results.Where(result => !result.IsSuccess),
            result => Assert.Equal(RelayStoreFailure.InvitationInvalid, result.Failure));
    }

    [Fact]
    public async Task RepeatedCreateRequestsReturnOneRelay()
    {
        var owner = await this.stores.CreateAccountAsync();
        var key = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            await this.relays.CreateAsync(
                owner,
                "Repeated",
                "REPEATED",
                key,
                DateTimeOffset.UtcNow,
                RelayOptions.Default,
                CancellationToken.None))));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Failure?.ToString()));
        Assert.Single(results.Select(result => result.Value!.RelayId).Distinct());
    }

    [Fact]
    public async Task MixedOwnerAndMemberChangesNeverThrow()
    {
        var (owner, relay) = await this.CreateRelayAsync();
        var joins = await this.PrepareJoinsAsync(owner, relay.RelayId, 8);
        foreach (var join in joins.Take(4))
            Assert.True((await this.JoinAsync(join, RelayOptions.Default)).IsSuccess);
        var members = (await this.relays.ListMembersAsync(owner, relay.RelayId, CancellationToken.None)).Value!;
        var kicked = members.Where(member => member.AccountId == joins[0].Account || member.AccountId == joins[1].Account);

        var operations = new List<Task<bool>>();
        operations.AddRange(joins.Skip(4).Select(async join =>
            (await this.JoinAsync(join, RelayOptions.Default)).IsSuccess));
        operations.AddRange(kicked.Select(member => Task.Run(async () => (await this.relays.KickAsync(
            owner,
            relay.RelayId,
            member.MembershipId,
            DateTimeOffset.UtcNow,
            CancellationToken.None)).IsSuccess)));
        operations.AddRange(joins.Skip(2).Take(2).Select(join => Task.Run(async () => (await this.relays.LeaveAsync(
            join.Account,
            relay.RelayId,
            DateTimeOffset.UtcNow,
            CancellationToken.None)).IsSuccess)));
        operations.Add(Task.Run(async () => (await this.relays.RenameAsync(
            owner,
            relay.RelayId,
            "Renamed",
            "RENAMED",
            DateTimeOffset.UtcNow,
            CancellationToken.None)).IsSuccess));

        Assert.All(await Task.WhenAll(operations), Assert.True);
        var stored = (await this.relays.GetAsync(owner, relay.RelayId, CancellationToken.None)).Value!;
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal(5, stored.MemberCount);
    }

    [Fact]
    public async Task KickedMemberNeedsANewInvitation()
    {
        var (owner, relay) = await this.CreateRelayAsync();
        var join = (await this.PrepareJoinsAsync(owner, relay.RelayId, 1))[0];
        var oldInvitation = await this.InviteAsync(owner, relay.RelayId);
        Assert.True((await this.JoinAsync(join, RelayOptions.Default)).IsSuccess);
        var member = (await this.relays.ListMembersAsync(owner, relay.RelayId, CancellationToken.None))
            .Value!.Single(candidate => candidate.AccountId == join.Account);

        Assert.True((await this.relays.KickAsync(
            owner,
            relay.RelayId,
            member.MembershipId,
            DateTimeOffset.UtcNow,
            CancellationToken.None)).IsSuccess);

        Assert.Null(await this.relays.GetMembershipRevisionsAsync(
            join.Account,
            [relay.RelayId],
            CancellationToken.None));
        Assert.Equal(
            RelayStoreFailure.RemovalRestricted,
            (await this.JoinAsync((join.Account, oldInvitation), RelayOptions.Default)).Failure);
        var newInvitation = await this.InviteAsync(owner, relay.RelayId);
        Assert.True((await this.JoinAsync((join.Account, newInvitation), RelayOptions.Default)).IsSuccess);
    }

    [Fact]
    public async Task DeletedRelayLosesMembersAndInvitations()
    {
        var (owner, relay) = await this.CreateRelayAsync();
        var joins = await this.PrepareJoinsAsync(owner, relay.RelayId, 2);
        Assert.True((await this.JoinAsync(joins[0], RelayOptions.Default)).IsSuccess);

        Assert.True((await this.relays.DeleteAsync(
            owner,
            relay.RelayId,
            DateTimeOffset.UtcNow,
            CancellationToken.None)).IsSuccess);

        Assert.Empty(await this.relays.ListAsync(joins[0].Account, CancellationToken.None));
        Assert.Equal(
            RelayStoreFailure.InvitationInvalid,
            (await this.JoinAsync(joins[1], RelayOptions.Default)).Failure);
        await using var context = await this.stores.Contexts.CreateDbContextAsync();
        Assert.False(await context.RelayMemberships.AnyAsync(member => member.RelayId == relay.RelayId));
    }

    private async Task<(AccountId Owner, StoredRelay Relay)> CreateRelayAsync()
    {
        var owner = await this.stores.CreateAccountAsync();
        var created = await this.relays.CreateAsync(
            owner,
            "Test Relay",
            "TEST RELAY",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            RelayOptions.Default,
            CancellationToken.None);
        return (owner, created.Value!);
    }

    private async Task<(AccountId Account, string TokenHash)[]> PrepareJoinsAsync(
        AccountId owner,
        Guid relayId,
        int count)
    {
        var joins = new (AccountId, string)[count];
        for (var index = 0; index < count; index++)
            joins[index] = (await this.stores.CreateAccountAsync(), await this.InviteAsync(owner, relayId));
        return joins;
    }

    private async Task<string> InviteAsync(AccountId owner, Guid relayId)
    {
        var hash = $"{Guid.NewGuid():N}{Guid.NewGuid():N}".ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var invitation = await this.relays.CreateInvitationAsync(
            owner,
            relayId,
            hash,
            now,
            now.AddDays(1),
            RelayOptions.Default,
            CancellationToken.None);
        Assert.True(invitation.IsSuccess);
        return hash;
    }

    private Task<RelayStoreResult<StoredRelay>> JoinAsync(
        (AccountId Account, string TokenHash) join,
        RelayOptions limits) =>
        Task.Run(async () => await this.relays.AcceptInvitationAsync(
            join.Account,
            join.TokenHash,
            DateTimeOffset.UtcNow,
            limits,
            CancellationToken.None));
}
