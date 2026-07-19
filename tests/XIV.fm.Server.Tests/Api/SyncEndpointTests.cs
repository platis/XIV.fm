using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Infrastructure.Presence;

namespace XIV.fm.Server.Tests.Api;

public sealed class SyncEndpointTests : IClassFixture<ServerApiFactory>
{
    private readonly ServerApiFactory factory;

    public SyncEndpointTests(ServerApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HealthEndpointsAreAvailableWithoutAuthentication()
    {
        using var client = this.factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.NotEmpty(live.Headers.GetValues("X-Request-ID"));
        Assert.NotEmpty(ready.Headers.GetValues("X-Request-ID"));
    }

    [Fact]
    public async Task MissingCredentialReturnsStructuredUnauthorizedError()
    {
        using var client = this.factory.CreateClient();

        using var response = await client.PostAsJsonAsync(ApiRoutes.Sync, CreateRequest());
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("installation_credential_required", error.Code);
        Assert.Equal(error.RequestId, Assert.Single(response.Headers.GetValues("X-Request-ID")));
    }

    [Fact]
    public async Task AuthenticatedPrivateSyncStoresHeartbeatAndReturnsCachedEmptyState()
    {
        using var client = this.CreateAuthenticatedClient();
        var before = DateTimeOffset.UtcNow;

        using var response = await client.PostAsJsonAsync(ApiRoutes.Sync, CreateRequest());
        var sync = await response.Content.ReadFromJsonAsync<SyncResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(sync);
        Assert.Equal(ListeningStatus.Unavailable, sync.OwnListening.Status);
        Assert.Null(sync.OwnListening.Track);
        Assert.Equal(30, sync.NextSyncAfterSeconds);
        Assert.NotNull(sync.LocationPresence.Snapshot);
        Assert.Empty(sync.LocationPresence.Snapshot.Entries);
        Assert.True(sync.PresenceExpiresAt >= before.AddSeconds(55));

        var store = this.factory.Services.GetRequiredService<InMemoryPresenceStore>();
        Assert.True(store.TryGet(ServerApiFactory.InstallationId, out var heartbeat));
        Assert.NotNull(heartbeat);
        Assert.Equal("Alice Cat", heartbeat.Character.Name);
        Assert.Equal(XIV.fm.Server.Domain.Presence.VisibilityMode.Private, heartbeat.Visibility.Mode);
    }

    [Fact]
    public async Task KnownSnapshotVersionSuppressesUnchangedSnapshotBody()
    {
        using var client = this.CreateAuthenticatedClient();
        using var firstResponse = await client.PostAsJsonAsync(ApiRoutes.Sync, CreateRequest());
        var first = await firstResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(first);

        var nextRequest = CreateRequest() with { KnownSnapshotVersion = first.LocationPresence.Version };
        using var secondResponse = await client.PostAsJsonAsync(ApiRoutes.Sync, nextRequest);
        var second = await secondResponse.Content.ReadFromJsonAsync<SyncResponse>();

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(second);
        Assert.Equal(first.LocationPresence.Version, second.LocationPresence.Version);
        Assert.Null(second.LocationPresence.Snapshot);
    }

    [Fact]
    public async Task InvalidLocationReturnsBoundedValidationErrors()
    {
        using var client = this.CreateAuthenticatedClient();
        var request = CreateRequest() with
        {
            Location = new LocationScope(0, 0, 0, 0),
        };

        using var response = await client.PostAsJsonAsync(ApiRoutes.Sync, request);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains("location.currentWorldId", error.Errors!.Keys);
        Assert.Contains("location.territoryId", error.Errors.Keys);
        Assert.Contains("location.mapId", error.Errors.Keys);
    }

    [Fact]
    public async Task CustomVisibilityFailsClosedUntilMembershipAuthorizationExists()
    {
        using var client = this.CreateAuthenticatedClient();
        var request = CreateRequest() with
        {
            Visibility = new VisibilitySelection(VisibilityMode.Custom, [Guid.NewGuid()]),
        };

        using var response = await client.PostAsJsonAsync(ApiRoutes.Sync, request);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("custom_relays_not_available", error.Code);
    }

    [Fact]
    public async Task MalformedJsonReturnsStructuredBadRequest()
    {
        using var client = this.CreateAuthenticatedClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(ApiRoutes.Sync, content);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("invalid_request", error.Code);
    }

    [Fact]
    public async Task ValidCallerRequestIdIsReturned()
    {
        using var client = this.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add("X-Request-ID", "test-request-42");

        using var response = await client.PostAsJsonAsync(ApiRoutes.Sync, CreateRequest());

        Assert.Equal("test-request-42", Assert.Single(response.Headers.GetValues("X-Request-ID")));
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = this.factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ServerApiFactory.Credential);
        return client;
    }

    private static SyncRequest CreateRequest() => new(
        "0.1.2.0",
        new CharacterIdentity("Alice Cat", 54),
        new LocationScope(63, 129, 130, 2),
        new VisibilitySelection(VisibilityMode.Private, []),
        null);
}
