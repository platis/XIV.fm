using Microsoft.Extensions.DependencyInjection;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Network;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Tests.Api;

public sealed class RelayApiClientTests
{
    [Fact]
    public async Task TypedClientSupportsTheRelayManagementLifecycle()
    {
        await using var factory = new ServerApiFactory();
        var ownerCredential = await CreateLinkedCredentialAsync(factory, "TypedOwner", "typed-owner");
        var memberCredential = await CreateLinkedCredentialAsync(factory, "TypedMember", "typed-member");
        var kickedCredential = await CreateLinkedCredentialAsync(factory, "TypedKicked", "typed-kicked");
        using var ownerHttp = factory.CreateClient();
        using var memberHttp = factory.CreateClient();
        using var kickedHttp = factory.CreateClient();
        using var owner = new ServerSyncApiClient(ownerHttp);
        using var member = new ServerSyncApiClient(memberHttp);
        using var kicked = new ServerSyncApiClient(kickedHttp);
        var baseUri = ownerHttp.BaseAddress!;

        var relay = await owner.CreateRelayAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            new CreateRelayRequest("Typed Relay", Guid.NewGuid()),
            CancellationToken.None);
        Assert.True(relay.IsOwner);
        Assert.Equal(relay.RelayId, Assert.Single((await owner.ListRelaysAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            CancellationToken.None)).Relays).RelayId);

        var renamed = await owner.RenameRelayAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            new RenameRelayRequest("Renamed Typed Relay"),
            CancellationToken.None);
        Assert.Equal("Renamed Typed Relay", renamed.Name);

        var firstInvitation = await owner.CreateRelayInvitationAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            new CreateRelayInvitationRequest(),
            CancellationToken.None);
        var preview = await member.PreviewRelayInvitationAsync(
            baseUri,
            memberCredential,
            "0.1.24.0",
            new RelayInvitationTokenRequest(firstInvitation.Token),
            CancellationToken.None);
        Assert.Equal(relay.RelayId, preview.RelayId);
        var accepted = await member.AcceptRelayInvitationAsync(
            baseUri,
            memberCredential,
            "0.1.24.0",
            new RelayInvitationTokenRequest(firstInvitation.Token),
            CancellationToken.None);
        Assert.Equal(relay.RelayId, accepted.Relay.RelayId);

        var secondInvitation = await owner.CreateRelayInvitationAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            new CreateRelayInvitationRequest(),
            CancellationToken.None);
        await kicked.AcceptRelayInvitationAsync(
            baseUri,
            kickedCredential,
            "0.1.24.0",
            new RelayInvitationTokenRequest(secondInvitation.Token),
            CancellationToken.None);
        var members = await owner.ListRelayMembersAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            CancellationToken.None);
        var kickedMembership = Assert.Single(members.Members, candidate => candidate.LastFmAccountName == "TypedKicked");
        await owner.KickRelayMemberAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            kickedMembership.MembershipId,
            CancellationToken.None);

        await member.LeaveRelayAsync(
            baseUri,
            memberCredential,
            "0.1.24.0",
            relay.RelayId,
            CancellationToken.None);

        var unusedInvitation = await owner.CreateRelayInvitationAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            new CreateRelayInvitationRequest(),
            CancellationToken.None);
        Assert.Contains(
            (await owner.ListRelayInvitationsAsync(
                baseUri,
                ownerCredential,
                "0.1.24.0",
                relay.RelayId,
                CancellationToken.None)).Invitations,
            invitation => invitation.InvitationId == unusedInvitation.InvitationId);
        await owner.RevokeRelayInvitationAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            unusedInvitation.InvitationId,
            CancellationToken.None);
        await owner.DeleteRelayAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            relay.RelayId,
            CancellationToken.None);
        Assert.Empty((await owner.ListRelaysAsync(
            baseUri,
            ownerCredential,
            "0.1.24.0",
            CancellationToken.None)).Relays);
    }

    private static async Task<string> CreateLinkedCredentialAsync(
        ServerApiFactory factory,
        string accountName,
        string discriminator)
    {
        var store = factory.Services.GetRequiredService<IAccountLinkStore>();
        var sessionId = new AccountLinkSessionId(Guid.NewGuid());
        var credential = $"relay-client-{discriminator}-credential-000000000000000000000000";
        var state = $"relay-client-{discriminator}-state-0000000000000000000000000000";
        var providerToken = $"relay-client-{discriminator}-provider-token-00000000000000000000";
        var now = DateTimeOffset.UtcNow;
        await store.CreateAsync(
            new NewAccountLinkSession(
                sessionId,
                credential,
                state,
                providerToken,
                now,
                now.AddMinutes(10)),
            CancellationToken.None);
        Assert.True(await store.TryClaimAuthorizationAsync(
            sessionId,
            state,
            providerToken,
            now,
            CancellationToken.None));
        await store.CompleteAsync(
            sessionId,
            new LastFmAccountIdentity(accountName),
            now,
            CancellationToken.None);
        return credential;
    }
}
