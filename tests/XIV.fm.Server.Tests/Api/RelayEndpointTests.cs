using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Tests.Api;

public sealed class RelayEndpointTests
{
    [Fact]
    public async Task InvitationMembershipCustomPresenceAndKickAreEnforcedImmediately()
    {
        await using var factory = new ServerApiFactory();
        var ownerCredential = await CreateLinkedCredentialAsync(factory, "RelayOwner", "owner");
        var memberCredential = await CreateLinkedCredentialAsync(factory, "RelayMember", "member");
        using var owner = CreateClient(factory, ownerCredential);
        using var member = CreateClient(factory, memberCredential);

        using var createdResponse = await owner.PostAsJsonAsync(ApiRoutes.Relays, new CreateRelayRequest("Late Night Listeners", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var relay = await createdResponse.Content.ReadFromJsonAsync<RelayResponse>();
        Assert.NotNull(relay);
        Assert.True(relay.IsOwner);
        Assert.Equal(1, relay.MemberCount);

        var invitation = await CreateInvitationAsync(owner, relay.RelayId);
        using var previewResponse = await member.PostAsJsonAsync(ApiRoutes.RelayInvitationPreview, new RelayInvitationTokenRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<RelayInvitationPreviewResponse>();
        Assert.Equal(relay.RelayId, preview!.RelayId);

        using var acceptedResponse = await member.PostAsJsonAsync(ApiRoutes.RelayInvitationAccept, new RelayInvitationTokenRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        using var acceptedAgainResponse = await member.PostAsJsonAsync(ApiRoutes.RelayInvitationAccept, new RelayInvitationTokenRequest(invitation.Token));
        Assert.Equal(HttpStatusCode.OK, acceptedAgainResponse.StatusCode);

        using var ownerSyncResponse = await owner.PostAsJsonAsync(ApiRoutes.Sync, CreateCustomSync("Owner Cat", 54, relay.RelayId));
        Assert.Equal(HttpStatusCode.OK, ownerSyncResponse.StatusCode);
        using var memberSyncResponse = await member.PostAsJsonAsync(ApiRoutes.Sync, CreateCustomSync("Member Cat", 55, relay.RelayId));
        Assert.Equal(HttpStatusCode.OK, memberSyncResponse.StatusCode);
        var memberSync = await memberSyncResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(memberSync);
        Assert.Equal(2, memberSync.LocationPresence.Snapshot!.Entries.Count);

        var unusedBeforeKick = await CreateInvitationAsync(owner, relay.RelayId);
        using var membersResponse = await owner.GetAsync($"/v1/relays/{relay.RelayId:D}/members");
        var members = await membersResponse.Content.ReadFromJsonAsync<RelayMemberListResponse>();
        var target = Assert.Single(members!.Members, candidate => !candidate.IsOwner);
        using var kickResponse = await owner.DeleteAsync($"/v1/relays/{relay.RelayId:D}/members/{target.MembershipId:D}");
        Assert.Equal(HttpStatusCode.NoContent, kickResponse.StatusCode);

        using var rebuiltOwnerResponse = await owner.PostAsJsonAsync(
            ApiRoutes.Sync,
            CreateCustomSync("Owner Cat", 54, relay.RelayId) with
            {
                KnownSnapshotVersion = memberSync.LocationPresence.Version,
            });
        var rebuiltOwner = await rebuiltOwnerResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(rebuiltOwner);
        Assert.Single(rebuiltOwner.LocationPresence.Snapshot!.Entries);
        Assert.NotEqual(memberSync.LocationPresence.Version, rebuiltOwner.LocationPresence.Version);

        using var kickedSyncResponse = await member.PostAsJsonAsync(ApiRoutes.Sync, CreateCustomSync("Member Cat", 55, relay.RelayId));
        Assert.Equal(HttpStatusCode.Forbidden, kickedSyncResponse.StatusCode);
        var kickedError = await kickedSyncResponse.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("relay_membership_required", kickedError!.Code);

        using var oldInvitationResponse = await member.PostAsJsonAsync(ApiRoutes.RelayInvitationAccept, new RelayInvitationTokenRequest(unusedBeforeKick.Token));
        Assert.Equal(HttpStatusCode.Forbidden, oldInvitationResponse.StatusCode);
        var oldInvitationError = await oldInvitationResponse.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("relay_rejoin_restricted", oldInvitationError!.Code);

        var explicitReinvite = await CreateInvitationAsync(owner, relay.RelayId);
        using var rejoinResponse = await member.PostAsJsonAsync(ApiRoutes.RelayInvitationAccept, new RelayInvitationTokenRequest(explicitReinvite.Token));
        Assert.Equal(HttpStatusCode.OK, rejoinResponse.StatusCode);
    }

    [Fact]
    public async Task MemberCannotMutateRelayAndOwnerCannotLeaveOrKickSelf()
    {
        await using var factory = new ServerApiFactory();
        var ownerCredential = await CreateLinkedCredentialAsync(factory, "OwnerTwo", "owner-two");
        var memberCredential = await CreateLinkedCredentialAsync(factory, "MemberTwo", "member-two");
        using var owner = CreateClient(factory, ownerCredential);
        using var member = CreateClient(factory, memberCredential);
        var relayResponse = await owner.PostAsJsonAsync(ApiRoutes.Relays, new CreateRelayRequest("Authorization Relay", Guid.NewGuid()));
        var relay = await relayResponse.Content.ReadFromJsonAsync<RelayResponse>();
        var invitation = await CreateInvitationAsync(owner, relay!.RelayId);
        await member.PostAsJsonAsync(ApiRoutes.RelayInvitationAccept, new RelayInvitationTokenRequest(invitation.Token));

        using var renameResponse = await member.PatchAsJsonAsync($"/v1/relays/{relay.RelayId:D}", new RenameRelayRequest("Stolen Name"));
        Assert.Equal(HttpStatusCode.Forbidden, renameResponse.StatusCode);
        using var ownerLeaveResponse = await owner.DeleteAsync($"/v1/relays/{relay.RelayId:D}/membership");
        Assert.Equal(HttpStatusCode.Conflict, ownerLeaveResponse.StatusCode);

        var members = await (await owner.GetAsync($"/v1/relays/{relay.RelayId:D}/members")).Content.ReadFromJsonAsync<RelayMemberListResponse>();
        var ownerMembership = Assert.Single(members!.Members, candidate => candidate.IsOwner);
        using var kickOwnerResponse = await owner.DeleteAsync($"/v1/relays/{relay.RelayId:D}/members/{ownerMembership.MembershipId:D}");
        Assert.Equal(HttpStatusCode.Conflict, kickOwnerResponse.StatusCode);
    }

    [Fact]
    public async Task CreationNormalizesNamesAndEnforcesIdempotencyAndBurstQuota()
    {
        await using var factory = new ServerApiFactory();
        var credential = await CreateLinkedCredentialAsync(factory, "QuotaOwner", "quota-owner");
        using var owner = CreateClient(factory, credential);
        var key = Guid.NewGuid();

        using var createdResponse = await owner.PostAsJsonAsync(ApiRoutes.Relays, new CreateRelayRequest("  Cafe\u0301 Relay  ", key));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<RelayResponse>();
        Assert.Equal("Café Relay", created!.Name);

        using var replayResponse = await owner.PostAsJsonAsync(ApiRoutes.Relays, new CreateRelayRequest("Café Relay", key));
        var replay = await replayResponse.Content.ReadFromJsonAsync<RelayResponse>();
        Assert.Equal(created.RelayId, replay!.RelayId);

        using var changedReplayResponse = await owner.PostAsJsonAsync(ApiRoutes.Relays, new CreateRelayRequest("Different Relay", key));
        Assert.Equal(HttpStatusCode.Conflict, changedReplayResponse.StatusCode);
        using var burstResponse = await owner.PostAsJsonAsync(ApiRoutes.Relays, new CreateRelayRequest("Another Relay", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.TooManyRequests, burstResponse.StatusCode);
    }

    [Fact]
    public async Task OneInvitationCannotBeConsumedByTwoAccountsRacing()
    {
        await using var factory = new ServerApiFactory();
        var ownerCredential = await CreateLinkedCredentialAsync(factory, "RaceOwner", "race-owner");
        var firstCredential = await CreateLinkedCredentialAsync(factory, "RaceOne", "race-one");
        var secondCredential = await CreateLinkedCredentialAsync(factory, "RaceTwo", "race-two");
        using var owner = CreateClient(factory, ownerCredential);
        using var first = CreateClient(factory, firstCredential);
        using var second = CreateClient(factory, secondCredential);
        var relay = await (await owner.PostAsJsonAsync(ApiRoutes.Relays, new CreateRelayRequest("Race Relay", Guid.NewGuid()))).Content.ReadFromJsonAsync<RelayResponse>();
        var invitation = await CreateInvitationAsync(owner, relay!.RelayId);

        var attempts = await Task.WhenAll(
            first.PostAsJsonAsync(ApiRoutes.RelayInvitationAccept, new RelayInvitationTokenRequest(invitation.Token)),
            second.PostAsJsonAsync(ApiRoutes.RelayInvitationAccept, new RelayInvitationTokenRequest(invitation.Token)));
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.NotFound);
        foreach (var response in attempts)
            response.Dispose();
    }

    private static async Task<CreatedRelayInvitationResponse> CreateInvitationAsync(HttpClient owner, Guid relayId)
    {
        using var response = await owner.PostAsJsonAsync($"/v1/relays/{relayId:D}/invitations", new CreateRelayInvitationRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedRelayInvitationResponse>())!;
    }

    private static SyncRequest CreateCustomSync(string name, uint homeWorldId, Guid relayId) => new(
        "0.1.3.0",
        new CharacterIdentity(name, homeWorldId),
        new LocationScope(63, 129, 130, 2),
        new VisibilitySelection(VisibilityMode.Custom, [relayId]),
        null);

    private static HttpClient CreateClient(ServerApiFactory factory, string credential)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return client;
    }

    private static async Task<string> CreateLinkedCredentialAsync(ServerApiFactory factory, string accountName, string discriminator)
    {
        var store = factory.Services.GetRequiredService<IAccountLinkStore>();
        var sessionId = new AccountLinkSessionId(Guid.NewGuid());
        var credential = $"relay-{discriminator}-credential-000000000000000000000000";
        var state = $"relay-{discriminator}-state-0000000000000000000000000000";
        var providerToken = $"relay-{discriminator}-provider-token-00000000000000000000";
        var now = DateTimeOffset.UtcNow;
        await store.CreateAsync(new NewAccountLinkSession(sessionId, credential, state, providerToken, now, now.AddMinutes(10)), CancellationToken.None);
        Assert.True(await store.TryClaimAuthorizationAsync(sessionId, state, providerToken, now, CancellationToken.None));
        await store.CompleteAsync(sessionId, new LastFmAccountIdentity(accountName), now, CancellationToken.None);
        return credential;
    }
}
