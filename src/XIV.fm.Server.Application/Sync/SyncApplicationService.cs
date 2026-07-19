using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Listening;
using XIV.fm.Server.Application.Presence;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Domain.Listening;
using DomainCharacterIdentity = XIV.fm.Server.Domain.Presence.CharacterIdentity;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;
using DomainPresenceHeartbeat = XIV.fm.Server.Domain.Presence.PresenceHeartbeat;
using DomainVisibilityMode = XIV.fm.Server.Domain.Presence.VisibilityMode;
using DomainVisibilitySelection = XIV.fm.Server.Domain.Presence.VisibilitySelection;

namespace XIV.fm.Server.Application.Sync;

public sealed class SyncApplicationService
{
    private static readonly TimeSpan PresenceLifetime = TimeSpan.FromSeconds(60);
    private const int ResponsiveSyncAfterSeconds = 10;
    private const int DefaultSyncAfterSeconds = 30;

    private readonly IPresenceStore presenceStore;
    private readonly ILinkedAccountResolver linkedAccountResolver;
    private readonly IListeningStateStore listeningStateStore;
    private readonly IListeningPollingCoordinator pollingCoordinator;
    private readonly ListeningFreshnessPolicy freshnessPolicy;
    private readonly PublicPresenceSnapshotService publicSnapshotService;
    private readonly RelayPresenceSnapshotService relaySnapshotService;
    private readonly IListeningPollingTelemetry listeningTelemetry;
    private readonly RelayOptions relayOptions;
    private readonly TimeProvider timeProvider;

    public SyncApplicationService(
        IPresenceStore presenceStore,
        ILinkedAccountResolver linkedAccountResolver,
        IListeningStateStore listeningStateStore,
        IListeningPollingCoordinator pollingCoordinator,
        ListeningFreshnessPolicy freshnessPolicy,
        PublicPresenceSnapshotService publicSnapshotService,
        RelayPresenceSnapshotService relaySnapshotService,
        IListeningPollingTelemetry listeningTelemetry,
        RelayOptions relayOptions,
        TimeProvider timeProvider)
    {
        this.presenceStore = presenceStore;
        this.linkedAccountResolver = linkedAccountResolver;
        this.listeningStateStore = listeningStateStore;
        this.pollingCoordinator = pollingCoordinator;
        this.freshnessPolicy = freshnessPolicy;
        this.publicSnapshotService = publicSnapshotService;
        this.relaySnapshotService = relaySnapshotService;
        this.listeningTelemetry = listeningTelemetry;
        this.relayOptions = relayOptions;
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
        if (request.Visibility.Mode == VisibilityMode.Custom && request.Visibility.RelayIds.Count > this.relayOptions.MaximumSelectedRelays)
        {
            return SyncResult.Failed(new SyncFailure(
                SyncFailureKind.Validation,
                "relay_selection_limit_exceeded",
                $"No more than {this.relayOptions.MaximumSelectedRelays} Relays may be selected."));
        }

        var linkedAccount = await this.linkedAccountResolver
            .GetForInstallationAsync(installationId, cancellationToken)
            .ConfigureAwait(false);
        if (request.Visibility.Mode is VisibilityMode.Public or VisibilityMode.Custom && linkedAccount is null)
        {
            return SyncResult.Failed(new SyncFailure(
                SyncFailureKind.Authorization,
                "linked_account_required",
                "A linked account is required for social presence."));
        }

        if (request.Visibility.Mode == VisibilityMode.Custom)
        {
            var membership = await this.relaySnapshotService.GetAuthorizedUnionAsync(
                linkedAccount!.AccountId,
                request.Visibility.RelayIds,
                new DomainLocationScope(
                    request.Location.CurrentWorldId,
                    request.Location.TerritoryId,
                    request.Location.MapId,
                    request.Location.InstanceId),
                cancellationToken).ConfigureAwait(false);
            if (membership is null)
            {
                return SyncResult.Failed(new SyncFailure(
                    SyncFailureKind.Authorization,
                    "relay_membership_required",
                    "Current membership in every selected Relay is required."));
            }
        }

        var now = this.timeProvider.GetUtcNow();
        var presenceExpiresAt = now.Add(PresenceLifetime);
        var heartbeat = new DomainPresenceHeartbeat(
            installationId,
            linkedAccount?.AccountId,
            new DomainCharacterIdentity(request.Character.Name, request.Character.HomeWorldId),
            new DomainLocationScope(
                request.Location.CurrentWorldId,
                request.Location.TerritoryId,
                request.Location.MapId,
                request.Location.InstanceId),
            new DomainVisibilitySelection(MapVisibility(request.Visibility.Mode), request.Visibility.RelayIds),
            now,
            presenceExpiresAt);

        var previousHeartbeat = await this.presenceStore
            .UpsertAsync(heartbeat, cancellationToken)
            .ConfigureAwait(false);
        var publicationChanged = previousHeartbeat is null ||
            previousHeartbeat.ExpiresAt <= now ||
            previousHeartbeat.Visibility.Mode != heartbeat.Visibility.Mode ||
            previousHeartbeat.Location != heartbeat.Location ||
            previousHeartbeat.Character != heartbeat.Character ||
            previousHeartbeat.AccountId != heartbeat.AccountId ||
            !previousHeartbeat.Visibility.RelayIds.Order().SequenceEqual(heartbeat.Visibility.RelayIds.Order());
        if (publicationChanged &&
            previousHeartbeat?.Visibility.Mode == DomainVisibilityMode.Public)
        {
            await this.publicSnapshotService
                .InvalidateAsync(previousHeartbeat.Location, cancellationToken)
                .ConfigureAwait(false);
        }
        if (publicationChanged && heartbeat.Visibility.Mode == DomainVisibilityMode.Public)
        {
            await this.publicSnapshotService
                .InvalidateAsync(heartbeat.Location, cancellationToken)
                .ConfigureAwait(false);
        }
        if (publicationChanged && previousHeartbeat?.Visibility.Mode == DomainVisibilityMode.Custom)
        {
            await this.relaySnapshotService
                .InvalidateAsync(previousHeartbeat.Visibility.RelayIds, previousHeartbeat.Location, cancellationToken)
                .ConfigureAwait(false);
        }
        if (publicationChanged && heartbeat.Visibility.Mode == DomainVisibilityMode.Custom)
        {
            await this.relaySnapshotService
                .InvalidateAsync(heartbeat.Visibility.RelayIds, heartbeat.Location, cancellationToken)
                .ConfigureAwait(false);
        }

        var ownListening = new ListeningState(ListeningStatus.Unavailable, false, null, null);
        if (linkedAccount is not null)
        {
            this.pollingCoordinator.NotifyActive(linkedAccount, presenceExpiresAt);
            var cached = await this.listeningStateStore
                .GetAsync(linkedAccount.AccountId, cancellationToken)
                .ConfigureAwait(false);
            this.listeningTelemetry.RecordCacheRead(cached is not null);
            if (cached is not null)
                ownListening = MapListeningState(cached, now);
        }

        SnapshotResult locationPresence;
        if (request.Visibility.Mode == VisibilityMode.Private)
        {
            locationPresence = PrivatePresenceSnapshot.Create(
                request.Location,
                now,
                request.KnownSnapshotVersion);
        }
        else if (request.Visibility.Mode == VisibilityMode.Public)
        {
            var sharedSnapshot = await this.publicSnapshotService
                .GetAsync(heartbeat.Location, cancellationToken)
                .ConfigureAwait(false);
            var snapshotVersion = PublicPresenceSnapshotService.CreateVersion(sharedSnapshot);
            locationPresence = new SnapshotResult(
                snapshotVersion,
                string.Equals(request.KnownSnapshotVersion, snapshotVersion, StringComparison.Ordinal)
                    ? null
                    : sharedSnapshot);
        }
        else
        {
            var authorized = await this.relaySnapshotService.GetAuthorizedUnionAsync(
                linkedAccount!.AccountId,
                request.Visibility.RelayIds,
                heartbeat.Location,
                cancellationToken).ConfigureAwait(false);
            if (authorized is null)
            {
                return SyncResult.Failed(new SyncFailure(
                    SyncFailureKind.Authorization,
                    "relay_membership_required",
                    "Current membership in every selected Relay is required."));
            }
            locationPresence = new SnapshotResult(
                authorized.Version,
                string.Equals(request.KnownSnapshotVersion, authorized.Version, StringComparison.Ordinal)
                    ? null
                    : authorized.Snapshot);
        }

        var nextSyncAfterSeconds =
            ownListening.Status == ListeningStatus.Playing && !ownListening.IsStale
                ? ResponsiveSyncAfterSeconds
                : DefaultSyncAfterSeconds;
        var response = new SyncResponse(
            now,
            presenceExpiresAt,
            nextSyncAfterSeconds,
            ownListening,
            locationPresence);
        return SyncResult.Success(response);
    }

    private ListeningState MapListeningState(ListeningObservation observation, DateTimeOffset now) =>
        new(
            observation.Status == ListeningObservationStatus.Playing
                ? ListeningStatus.Playing
                : ListeningStatus.NotPlaying,
            this.freshnessPolicy.IsStale(observation, now),
            observation.ObservedAt,
            observation.Track is null
                ? null
                : new Track(
                    observation.Track.Title,
                    observation.Track.Artist,
                    observation.Track.Album,
                    observation.Track.AlbumArtUrl,
                    observation.Track.TrackUrl,
                    observation.Track.StartedAt));

    private static DomainVisibilityMode MapVisibility(VisibilityMode mode) => mode switch
    {
        VisibilityMode.Private => DomainVisibilityMode.Private,
        VisibilityMode.Public => DomainVisibilityMode.Public,
        VisibilityMode.Custom => DomainVisibilityMode.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

}
