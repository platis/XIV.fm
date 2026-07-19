using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Tests.Api;

public sealed class PublicPresenceEndpointTests
{
    [Fact]
    public async Task MultipleAccountsReuseExactLocationSnapshotAndPrivateRemovalInvalidatesIt()
    {
        await using var factory = new ServerApiFactory();
        var aliceCredential = await CreateLinkedCredentialAsync(factory, "AliceListener", "alice");
        var bobCredential = await CreateLinkedCredentialAsync(factory, "BobListener", "bob");
        var otherInstanceCredential = await CreateLinkedCredentialAsync(factory, "OtherListener", "other");
        await SetPlayingAsync(factory, aliceCredential, "Alice Track");
        await SetPlayingAsync(factory, bobCredential, "Bob Track");
        await SetPlayingAsync(factory, otherInstanceCredential, "Other Track");
        using var alice = CreateClient(factory, aliceCredential);
        using var bob = CreateClient(factory, bobCredential);
        using var otherInstance = CreateClient(factory, otherInstanceCredential);

        using var aliceResponse = await alice.PostAsJsonAsync(
            ApiRoutes.Sync,
            CreateRequest("Alice Cat", 54, 2, VisibilityMode.Public));
        using var bobResponse = await bob.PostAsJsonAsync(
            ApiRoutes.Sync,
            CreateRequest("Alice Cat", 55, 2, VisibilityMode.Public));
        var bobSync = await bobResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(bobSync);
        Assert.Equal(2, bobSync.LocationPresence.Snapshot!.Entries.Count);
        Assert.Contains(
            bobSync.LocationPresence.Snapshot.Entries,
            entry => entry.Character.HomeWorldId == 54 && entry.Listening.Track!.Title == "Alice Track");
        Assert.Contains(
            bobSync.LocationPresence.Snapshot.Entries,
            entry => entry.Character.HomeWorldId == 55 && entry.Listening.Track!.Title == "Bob Track");

        using var otherResponse = await otherInstance.PostAsJsonAsync(
            ApiRoutes.Sync,
            CreateRequest("Other User", 56, 3, VisibilityMode.Public));
        var otherSync = await otherResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(otherSync);
        Assert.Single(otherSync.LocationPresence.Snapshot!.Entries);
        Assert.Equal("Other User", otherSync.LocationPresence.Snapshot.Entries[0].Character.Name);

        using var unchangedResponse = await alice.PostAsJsonAsync(
            ApiRoutes.Sync,
            CreateRequest("Alice Cat", 54, 2, VisibilityMode.Public) with
            {
                KnownSnapshotVersion = bobSync.LocationPresence.Version,
            });
        var unchanged = await unchangedResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(unchanged);
        Assert.Equal(bobSync.LocationPresence.Version, unchanged.LocationPresence.Version);
        Assert.Null(unchanged.LocationPresence.Snapshot);

        using var privateResponse = await bob.PostAsJsonAsync(
            ApiRoutes.Sync,
            CreateRequest("Alice Cat", 55, 2, VisibilityMode.Private));
        using var rebuiltResponse = await alice.PostAsJsonAsync(
            ApiRoutes.Sync,
            CreateRequest("Alice Cat", 54, 2, VisibilityMode.Public) with
            {
                KnownSnapshotVersion = bobSync.LocationPresence.Version,
            });
        var rebuilt = await rebuiltResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(rebuilt);
        var remaining = Assert.Single(rebuilt.LocationPresence.Snapshot!.Entries);
        Assert.Equal(54U, remaining.Character.HomeWorldId);
        Assert.NotEqual(bobSync.LocationPresence.Version, rebuilt.LocationPresence.Version);
    }

    private static async Task<string> CreateLinkedCredentialAsync(
        ServerApiFactory factory,
        string accountName,
        string discriminator)
    {
        var store = factory.Services.GetRequiredService<IAccountLinkStore>();
        var sessionId = new AccountLinkSessionId(Guid.NewGuid());
        var credential = $"linked-{discriminator}-credential-000000000000000000000000";
        var state = $"linked-{discriminator}-state-0000000000000000000000000000";
        var providerToken = $"linked-{discriminator}-provider-token-00000000000000000000";
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

    private static async Task SetPlayingAsync(
        ServerApiFactory factory,
        string credential,
        string title)
    {
        var credentials = factory.Services.GetRequiredService<IInstallationCredentialStore>();
        var installationId = await credentials.AuthenticateAsync(credential, CancellationToken.None);
        Assert.NotNull(installationId);
        var resolver = factory.Services.GetRequiredService<ILinkedAccountResolver>();
        var account = await resolver.GetForInstallationAsync(installationId.Value, CancellationToken.None);
        Assert.NotNull(account);
        var listening = factory.Services.GetRequiredService<IListeningStateStore>();
        await listening.SetAsync(
            account.AccountId,
            new ListeningObservation(
                ListeningObservationStatus.Playing,
                DateTimeOffset.UtcNow,
                new NormalizedTrack(title, "Artist", null, null, null, null)),
            CancellationToken.None);
    }

    private static HttpClient CreateClient(ServerApiFactory factory, string credential)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return client;
    }

    private static SyncRequest CreateRequest(
        string characterName,
        uint homeWorldId,
        uint instanceId,
        VisibilityMode visibility) => new(
            "0.1.3.0",
            new CharacterIdentity(characterName, homeWorldId),
            new LocationScope(63, 129, 130, instanceId),
            new VisibilitySelection(visibility, []),
            null);
}
