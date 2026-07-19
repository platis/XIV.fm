using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.AccountLinks;
using XIV.fm.Server.Infrastructure.Authentication;
using XIV.fm.Server.Infrastructure.Relays;

namespace XIV.fm.Server.Tests.Relays;

public sealed class InMemoryRelayStoreTests
{
    [Fact]
    public async Task DeletedRelaysStillCountTowardRollingCreationQuotaAndIdempotencyIsStable()
    {
        var store = CreateStore();
        var owner = new AccountId(Guid.NewGuid());
        var options = RelayOptions.Default with
        {
            MaximumActiveOwnedRelays = 1,
            MaximumCreationsPerRollingWindow = 2,
            CreationBurstWindow = TimeSpan.Zero,
        };
        var now = DateTimeOffset.UtcNow;
        var key = Guid.NewGuid();
        var first = await store.CreateAsync(owner, "First Relay", "FIRST RELAY", key, now, options, CancellationToken.None);
        var replay = await store.CreateAsync(owner, "First Relay", "FIRST RELAY", key, now, options, CancellationToken.None);
        Assert.Equal(first.Value!.RelayId, replay.Value!.RelayId);
        Assert.True((await store.DeleteAsync(owner, first.Value.RelayId, now.AddSeconds(1), CancellationToken.None)).IsSuccess);

        var second = await store.CreateAsync(owner, "Second Relay", "SECOND RELAY", Guid.NewGuid(), now.AddSeconds(2), options, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.True((await store.DeleteAsync(owner, second.Value!.RelayId, now.AddSeconds(3), CancellationToken.None)).IsSuccess);
        var blocked = await store.CreateAsync(owner, "Third Relay", "THIRD RELAY", Guid.NewGuid(), now.AddSeconds(4), options, CancellationToken.None);
        Assert.Equal(RelayStoreFailure.RollingCreationLimit, blocked.Failure);
    }

    [Fact]
    public async Task MemberAndInvitationLimitsFailClosed()
    {
        var store = CreateStore();
        var owner = new AccountId(Guid.NewGuid());
        var firstMember = new AccountId(Guid.NewGuid());
        var secondMember = new AccountId(Guid.NewGuid());
        var options = RelayOptions.Default with
        {
            MaximumMembersPerRelay = 2,
            MaximumActiveInvitationsPerRelay = 1,
            CreationBurstWindow = TimeSpan.Zero,
        };
        var now = DateTimeOffset.UtcNow;
        var relay = (await store.CreateAsync(owner, "Bounded Relay", "BOUNDED RELAY", Guid.NewGuid(), now, options, CancellationToken.None)).Value!;
        var firstInvite = await store.CreateInvitationAsync(owner, relay.RelayId, "HASH-ONE", now, now.AddDays(1), options, CancellationToken.None);
        Assert.True(firstInvite.IsSuccess);
        var inviteLimit = await store.CreateInvitationAsync(owner, relay.RelayId, "HASH-TWO", now, now.AddDays(1), options, CancellationToken.None);
        Assert.Equal(RelayStoreFailure.InvitationLimit, inviteLimit.Failure);
        Assert.True((await store.AcceptInvitationAsync(firstMember, "HASH-ONE", now.AddSeconds(1), options, CancellationToken.None)).IsSuccess);

        var secondInvite = await store.CreateInvitationAsync(owner, relay.RelayId, "HASH-THREE", now.AddSeconds(2), now.AddDays(1), options, CancellationToken.None);
        Assert.True(secondInvite.IsSuccess);
        var full = await store.AcceptInvitationAsync(secondMember, "HASH-THREE", now.AddSeconds(3), options, CancellationToken.None);
        Assert.Equal(RelayStoreFailure.MemberLimit, full.Failure);
    }

    private static InMemoryRelayStore CreateStore()
    {
        var credentialStore = new InMemoryInstallationCredentialStore();
        return new InMemoryRelayStore(new InMemoryAccountLinkStore(credentialStore));
    }
}
