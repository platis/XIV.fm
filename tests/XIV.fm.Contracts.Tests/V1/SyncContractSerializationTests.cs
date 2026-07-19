using System.Text.Json;
using XIV.fm.Contracts.V1;

namespace XIV.fm.Contracts.Tests.V1;

public sealed class SyncContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RequestUsesStableCamelCaseWireNamesAndStringEnums()
    {
        var relayId = Guid.Parse("5be0d1e2-0a63-4f16-ad4d-d53e95b7c97f");
        var request = new SyncRequest(
            "0.1.2.0",
            new CharacterIdentity("Alice Cat", 54),
            new LocationScope(63, 129, 130, 2),
            new VisibilitySelection(VisibilityMode.Custom, [relayId]),
            "snapshot-41");

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("0.1.2.0", root.GetProperty("pluginVersion").GetString());
        Assert.Equal("Alice Cat", root.GetProperty("character").GetProperty("name").GetString());
        Assert.Equal(54U, root.GetProperty("character").GetProperty("homeWorldId").GetUInt32());
        Assert.Equal("custom", root.GetProperty("visibility").GetProperty("mode").GetString());
        Assert.Equal(relayId, root.GetProperty("visibility").GetProperty("relayIds")[0].GetGuid());
        Assert.Equal("snapshot-41", root.GetProperty("knownSnapshotVersion").GetString());

        var roundTrip = JsonSerializer.Deserialize<SyncRequest>(json, JsonOptions);
        Assert.NotNull(roundTrip);
        Assert.Equal(VisibilityMode.Custom, roundTrip.Visibility.Mode);
        Assert.Equal(relayId, Assert.Single(roundTrip.Visibility.RelayIds));
    }

    [Fact]
    public void RequestContainsNoDutyStateOrCoordinates()
    {
        var request = new SyncRequest(
            "0.1.2.0",
            new CharacterIdentity("Alice Cat", 54),
            new LocationScope(63, 129, 130, 0),
            new VisibilitySelection(VisibilityMode.Private, []),
            null);

        var json = JsonSerializer.Serialize(request, JsonOptions);

        Assert.DoesNotContain("duty", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coordinate", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseRoundTripsAnUnchangedLocationSnapshot()
    {
        var response = new SyncResponse(
            new DateTimeOffset(2026, 7, 19, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 19, 8, 1, 0, TimeSpan.Zero),
            30,
            new ListeningState(ListeningStatus.NotPlaying, false, null, null),
            new SnapshotResult("snapshot-42", null));

        var json = JsonSerializer.Serialize(response, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<SyncResponse>(json, JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal(30, roundTrip.NextSyncAfterSeconds);
        Assert.Equal(ListeningStatus.NotPlaying, roundTrip.OwnListening.Status);
        Assert.Equal("snapshot-42", roundTrip.LocationPresence.Version);
        Assert.Null(roundTrip.LocationPresence.Snapshot);
    }

    [Fact]
    public void AccountLinkStatusUsesStableStringEnum()
    {
        var response = new AccountLinkStatusResponse(
            AccountLinkStatus.Linked,
            new DateTimeOffset(2026, 7, 19, 8, 10, 0, TimeSpan.Zero),
            "CanonicalListener");

        var json = JsonSerializer.Serialize(response, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<AccountLinkStatusResponse>(json, JsonOptions);

        Assert.Contains("\"status\":\"linked\"", json, StringComparison.Ordinal);
        Assert.NotNull(roundTrip);
        Assert.Equal(AccountLinkStatus.Linked, roundTrip.Status);
    }

    [Fact]
    public void RelayInvitationSecretHasAStableBoundedBodyShape()
    {
        var request = new RelayInvitationTokenRequest("invitation-token-000000000000000000000000");
        var json = JsonSerializer.Serialize(request, JsonOptions);

        Assert.Equal("{\"token\":\"invitation-token-000000000000000000000000\"}", json);
        Assert.DoesNotContain("relayId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutesAreVersioned()
    {
        var sessionId = Guid.Parse("2959650b-b6e1-4c3e-b37f-dfcd580f00d5");

        Assert.Equal("/v1/sync", ApiRoutes.Sync);
        Assert.Equal("/v1/account-links", ApiRoutes.StartAccountLink);
        Assert.Equal($"/v1/account-links/{sessionId:D}/status", ApiRoutes.GetAccountLinkStatus(sessionId));
        Assert.Equal($"/v1/account-links/{sessionId:D}/callback", ApiRoutes.GetAccountLinkCallback(sessionId));
        Assert.Equal("/v1/installations/current/credential", ApiRoutes.RotateCurrentInstallation);
        Assert.Equal("/v1/installations/current", ApiRoutes.RevokeCurrentInstallation);
        Assert.Equal("/v1/relays", ApiRoutes.Relays);
        Assert.Equal("/v1/relays/{relayId:guid}/members/{membershipId:guid}", ApiRoutes.RelayMember);
        Assert.Equal("/v1/relay-invitations/preview", ApiRoutes.RelayInvitationPreview);
        Assert.Equal("/v1/relay-invitations/accept", ApiRoutes.RelayInvitationAccept);
    }
}
