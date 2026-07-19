using System.Security.Cryptography;
using System.Text;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Installations;
using DomainCharacterIdentity = XIV.fm.Server.Domain.Presence.CharacterIdentity;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;
using DomainPresenceHeartbeat = XIV.fm.Server.Domain.Presence.PresenceHeartbeat;
using DomainVisibilityMode = XIV.fm.Server.Domain.Presence.VisibilityMode;
using DomainVisibilitySelection = XIV.fm.Server.Domain.Presence.VisibilitySelection;

namespace XIV.fm.Server.Application.Sync;

public sealed class SyncApplicationService
{
    private static readonly TimeSpan PresenceLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(20);
    private const int NextSyncAfterSeconds = 30;

    private readonly IPresenceStore presenceStore;
    private readonly TimeProvider timeProvider;

    public SyncApplicationService(IPresenceStore presenceStore, TimeProvider timeProvider)
    {
        this.presenceStore = presenceStore;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<SyncResult> SyncAsync(
        InstallationId installationId,
        SyncRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = SyncRequestValidator.Validate(request);
        if (errors.Count != 0)
        {
            return SyncResult.Failed(new SyncFailure(
                SyncFailureKind.Validation,
                "validation_failed",
                "The sync request is invalid.",
                Errors: errors));
        }

        ArgumentNullException.ThrowIfNull(request);
        if (request.Visibility.Mode == VisibilityMode.Custom)
        {
            return SyncResult.Failed(new SyncFailure(
                SyncFailureKind.Conflict,
                "custom_relays_not_available",
                "Custom Relays are not available yet.",
                "Relay membership authorization will be enabled in the Custom Relays phase."));
        }

        var now = this.timeProvider.GetUtcNow();
        var presenceExpiresAt = now.Add(PresenceLifetime);
        var heartbeat = new DomainPresenceHeartbeat(
            installationId,
            new DomainCharacterIdentity(request.Character.Name, request.Character.HomeWorldId),
            new DomainLocationScope(
                request.Location.CurrentWorldId,
                request.Location.TerritoryId,
                request.Location.MapId,
                request.Location.InstanceId),
            new DomainVisibilitySelection(MapVisibility(request.Visibility.Mode), request.Visibility.RelayIds),
            now,
            presenceExpiresAt);

        await this.presenceStore.UpsertAsync(heartbeat, cancellationToken).ConfigureAwait(false);

        var snapshotVersion = CreateEmptySnapshotVersion(request.Location);
        var snapshot = string.Equals(request.KnownSnapshotVersion, snapshotVersion, StringComparison.Ordinal)
            ? null
            : new PresenceSnapshot(
                request.Location,
                now,
                now.Add(SnapshotLifetime),
                []);

        var response = new SyncResponse(
            now,
            presenceExpiresAt,
            NextSyncAfterSeconds,
            new ListeningState(ListeningStatus.Unavailable, false, null, null),
            new SnapshotResult(snapshotVersion, snapshot));
        return SyncResult.Success(response);
    }

    private static DomainVisibilityMode MapVisibility(VisibilityMode mode) => mode switch
    {
        VisibilityMode.Private => DomainVisibilityMode.Private,
        VisibilityMode.Public => DomainVisibilityMode.Public,
        VisibilityMode.Custom => DomainVisibilityMode.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static string CreateEmptySnapshotVersion(LocationScope location)
    {
        var material = Encoding.UTF8.GetBytes(
            $"v1-empty:{location.CurrentWorldId}:{location.TerritoryId}:{location.MapId}:{location.InstanceId}");
        return Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant();
    }
}
