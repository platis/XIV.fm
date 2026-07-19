using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Network;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Listening;
using XIV.fm.Server.Infrastructure.Presence;

namespace XIV.fm.Server.Tests.Api;

public sealed class AccountLinkEndpointTests : IClassFixture<ServerApiFactory>
{
    private readonly ServerApiFactory factory;

    public AccountLinkEndpointTests(ServerApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LastFmProofLinksAccountAndPromotesLinkCredential()
    {
        using var client = this.factory.CreateClient();

        var started = await StartAsync(client);
        var authorizationQuery = ParseQuery(started.AuthorizationUrl);
        var providerToken = CreateProviderToken(started);
        var callback = new Uri(authorizationQuery["cb"]);

        using var callbackResponse = await client.GetAsync(
            $"{callback.PathAndQuery}&token={Uri.EscapeDataString(providerToken)}");
        Assert.Equal(HttpStatusCode.OK, callbackResponse.StatusCode);

        using var statusResponse = await client.PostAsJsonAsync(
            ApiRoutes.GetAccountLinkStatus(started.LinkSessionId),
            new AccountLinkStatusRequest(started.LinkCredential));
        var status = await statusResponse.Content.ReadFromJsonAsync<AccountLinkStatusResponse>();
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.NotNull(status);
        Assert.Equal(AccountLinkStatus.Linked, status.Status);
        Assert.Equal("CanonicalListener", status.LastFmAccountName);

        var credentialStore = this.factory.Services.GetRequiredService<IInstallationCredentialStore>();
        var installationId = await credentialStore.AuthenticateAsync(
            started.LinkCredential,
            CancellationToken.None);
        Assert.NotNull(installationId);
        var resolver = this.factory.Services.GetRequiredService<ILinkedAccountResolver>();
        var linkedAccount = await resolver.GetForInstallationAsync(
            installationId.Value,
            CancellationToken.None);
        Assert.NotNull(linkedAccount);
        var listeningStore = this.factory.Services.GetRequiredService<IListeningStateStore>();
        await listeningStore.SetAsync(
            linkedAccount.AccountId,
            new ListeningObservation(
                ListeningObservationStatus.Playing,
                DateTimeOffset.UtcNow,
                new NormalizedTrack(
                    "Test Track",
                    "Test Artist",
                    "Test Album",
                    new Uri("https://lastfm.test/art.jpg"),
                    new Uri("https://last.fm/music/test"),
                    null)),
            CancellationToken.None);

        using var authenticated = this.factory.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", started.LinkCredential);
        using var syncResponse = await authenticated.PostAsJsonAsync(ApiRoutes.Sync, CreateSyncRequest());
        var sync = await syncResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);
        Assert.NotNull(sync);
        Assert.Equal(ListeningStatus.Playing, sync.OwnListening.Status);
        Assert.False(sync.OwnListening.IsStale);
        Assert.Equal("Test Track", sync.OwnListening.Track?.Title);
        Assert.Equal(10, sync.NextSyncAfterSeconds);

        await listeningStore.SetAsync(
            linkedAccount.AccountId,
            new ListeningObservation(
                ListeningObservationStatus.Playing,
                DateTimeOffset.UtcNow.AddMinutes(-2),
                new NormalizedTrack("Stale Track", "Test Artist", null, null, null, null)),
            CancellationToken.None);
        using var staleResponse = await authenticated.PostAsJsonAsync(ApiRoutes.Sync, CreateSyncRequest());
        var stale = await staleResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(stale);
        Assert.Equal(ListeningStatus.Playing, stale.OwnListening.Status);
        Assert.True(stale.OwnListening.IsStale);
        Assert.Equal("Stale Track", stale.OwnListening.Track?.Title);
        Assert.Equal(30, stale.NextSyncAfterSeconds);

        var publicRequest = CreateSyncRequest() with
        {
            Visibility = new VisibilitySelection(VisibilityMode.Public, []),
        };
        using var publicResponse = await authenticated.PostAsJsonAsync(ApiRoutes.Sync, publicRequest);
        var publicSync = await publicResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(publicSync);
        var ownEntry = Assert.Single(publicSync.LocationPresence.Snapshot!.Entries);
        Assert.Equal("Alice Cat", ownEntry.Character.Name);
        Assert.Equal("Stale Track", ownEntry.Listening.Track?.Title);

        using var unchangedResponse = await authenticated.PostAsJsonAsync(
            ApiRoutes.Sync,
            publicRequest with { KnownSnapshotVersion = publicSync.LocationPresence.Version });
        var unchanged = await unchangedResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(unchanged);
        Assert.Equal(publicSync.LocationPresence.Version, unchanged.LocationPresence.Version);
        Assert.Null(unchanged.LocationPresence.Snapshot);

        using var privateResponse = await authenticated.PostAsJsonAsync(ApiRoutes.Sync, CreateSyncRequest());
        var presenceStore = this.factory.Services.GetRequiredService<InMemoryPresenceStore>();
        var remainingPublic = await presenceStore.GetPublicAsync(
            new XIV.fm.Server.Domain.Presence.LocationScope(63, 129, 130, 2),
            DateTimeOffset.UtcNow,
            500,
            CancellationToken.None);
        Assert.Empty(remainingPublic);
    }

    [Fact]
    public async Task TypedPluginClientStartsAndCompletesLinkStatusPolling()
    {
        using var transport = this.factory.CreateClient();
        using var apiClient = new ServerSyncApiClient(transport);

        var started = await apiClient.StartAccountLinkAsync(
            transport.BaseAddress!,
            "0.1.3.0",
            CancellationToken.None);
        var authorizationQuery = ParseQuery(started.AuthorizationUrl);
        var providerToken = CreateProviderToken(started);
        var callback = new Uri(authorizationQuery["cb"]);
        using var callbackResponse = await transport.GetAsync(
            $"{callback.PathAndQuery}&token={Uri.EscapeDataString(providerToken)}");
        var status = await apiClient.GetAccountLinkStatusAsync(
            transport.BaseAddress!,
            started.LinkSessionId,
            started.LinkCredential,
            "0.1.3.0",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, callbackResponse.StatusCode);
        Assert.Equal(AccountLinkStatus.Linked, status.Status);
        Assert.Equal("CanonicalListener", status.LastFmAccountName);
    }

    [Fact]
    public async Task CallbackStateIsSingleUseAndRejectsReplay()
    {
        using var client = this.factory.CreateClient();
        var started = await StartAsync(client);
        var authorizationQuery = ParseQuery(started.AuthorizationUrl);
        var providerToken = CreateProviderToken(started);
        var callback = new Uri(authorizationQuery["cb"]);

        using var invalid = await client.GetAsync(
            $"{callback.AbsolutePath}?state=wrong-state-value-0000000000000000000000&token={providerToken}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var validPath = $"{callback.PathAndQuery}&token={Uri.EscapeDataString(providerToken)}";
        using var accepted = await client.GetAsync(validPath);
        using var replayed = await client.GetAsync(validPath);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
    }

    [Fact]
    public async Task StatusRequiresTheSessionLinkCredential()
    {
        using var client = this.factory.CreateClient();
        var started = await StartAsync(client);

        using var response = await client.PostAsJsonAsync(
            ApiRoutes.GetAccountLinkStatus(started.LinkSessionId),
            new AccountLinkStatusRequest("wrong-link-credential-000000000000000000000"));
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("account_link_not_found", error.Code);
    }

    private static async Task<StartAccountLinkResponse> StartAsync(HttpClient client)
    {
        using var response = await client.PostAsync(ApiRoutes.StartAccountLink, null);
        var started = await response.Content.ReadFromJsonAsync<StartAccountLinkResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(started);
        Assert.True(started.LinkCredential.Length >= 32);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return started;
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) => uri.Query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            pair => Uri.UnescapeDataString(pair[0]),
            pair => Uri.UnescapeDataString(pair[1]),
            StringComparer.Ordinal);

    private static string CreateProviderToken(StartAccountLinkResponse started) =>
        $"lastfm-test-callback-token-{started.LinkSessionId:N}";

    private static SyncRequest CreateSyncRequest() => new(
        "0.1.3.0",
        new CharacterIdentity("Alice Cat", 54),
        new LocationScope(63, 129, 130, 2),
        new VisibilitySelection(VisibilityMode.Private, []),
        null);
}
