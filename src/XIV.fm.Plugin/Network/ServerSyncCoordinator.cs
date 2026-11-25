using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Adapters;
using XIV.fm.Plugin.Core.Overlay;
using XIV.fm.Plugin.Core.Policy;
using XIV.fm.Plugin.Core.Presence;
using XIV.fm.Plugin.Core.Sync;
using ContractCharacterIdentity = XIV.fm.Contracts.V1.CharacterIdentity;
using ContractLocationScope = XIV.fm.Contracts.V1.LocationScope;

namespace XIV.fm.Plugin.Network;

public sealed class ServerSyncCoordinator : IDisposable
{
    private readonly Lock gate = new();
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IServerSyncApiClient apiClient;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<ServerSyncSettings> settings;
    private readonly Func<VisibilitySelection> visibilitySelection;
    private readonly Action ownListeningChanged;
    private readonly Action refreshRelayMemberships;
    private readonly string pluginVersion;
    private readonly CancellationTokenSource disposalCancellation = new();
    private SyncRuntimeState state = SyncRuntimeState.Disabled;
    private CancellationTokenSource? activeRequest;
    private DateTimeOffset nextSyncAt = DateTimeOffset.MinValue;
    private string? knownSnapshotVersion;
    private readonly RemotePresenceStateStore remotePresence = new();
    private readonly OwnListeningStateStore ownListening = new();
    private int relayMembershipRejected;
    private int consecutiveFailures;
    private DateTimeOffset? lastSuccessAt;
    private string? lastError;
    private string? lastErrorCode;
    private long generation;
    private bool disposed;

    public ServerSyncCoordinator(
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IServerSyncApiClient apiClient,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<ServerSyncSettings> settings,
        Func<VisibilitySelection> visibilitySelection,
        Action ownListeningChanged,
        Action refreshRelayMemberships,
        string pluginVersion)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.apiClient = apiClient;
        this.dutyPolicy = dutyPolicy;
        this.settings = settings;
        this.visibilitySelection = visibilitySelection;
        this.ownListeningChanged = ownListeningChanged;
        this.refreshRelayMemberships = refreshRelayMemberships;
        this.pluginVersion = pluginVersion;
        this.framework.Update += this.OnFrameworkUpdate;
        this.clientState.Login += this.OnWake;
        this.clientState.Logout += this.OnLogout;
        this.clientState.TerritoryChanged += this.OnLocationChanged;
        this.clientState.MapIdChanged += this.OnLocationChanged;
        this.clientState.InstanceChanged += this.OnLocationChanged;
    }

    public SyncRuntimeState State => Volatile.Read(ref this.state);

    public ListeningState OwnListening => this.ownListening.Read(DateTimeOffset.UtcNow);

    public ImmutableArray<OverlayCard> GetRemoteCards(DateTimeOffset now) =>
        this.remotePresence.Read(now);

    public void RequestImmediateSync() => this.RestartAfterLifecycleChange();

    public void SuspendForAccountDisconnect()
    {
        this.CancelActiveRequest(SyncRuntimeStatus.Disabled, DateTimeOffset.UtcNow);
        if (this.ownListening.Clear(DateTimeOffset.UtcNow))
            this.ownListeningChanged();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.framework.Update -= this.OnFrameworkUpdate;
        this.clientState.Login -= this.OnWake;
        this.clientState.Logout -= this.OnLogout;
        this.clientState.TerritoryChanged -= this.OnLocationChanged;
        this.clientState.MapIdChanged -= this.OnLocationChanged;
        this.clientState.InstanceChanged -= this.OnLocationChanged;
        this.disposalCancellation.Cancel();
        this.CancelActiveRequest(SyncRuntimeStatus.Disabled, DateTimeOffset.UtcNow);
        this.disposalCancellation.Dispose();
        if (this.apiClient is IDisposable disposableClient)
            disposableClient.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTimeOffset.UtcNow;
        if (!this.dutyPolicy().AllowsServerRequests)
        {
            if (this.State.Status != SyncRuntimeStatus.SuspendedDuty)
            {
                this.CancelActiveRequest(SyncRuntimeStatus.SuspendedDuty, now);
            }

            return;
        }

        var currentSettings = this.settings();
        if (!TryValidateSettings(currentSettings, out var serverBaseUri))
        {
            if (this.State.Status != SyncRuntimeStatus.Disabled)
                this.CancelActiveRequest(SyncRuntimeStatus.Disabled, now);

            return;
        }

        if (Interlocked.Exchange(ref this.relayMembershipRejected, 0) != 0)
            this.refreshRelayMemberships();

        lock (this.gate)
        {
            if (this.activeRequest is not null || now < this.nextSyncAt)
                return;
        }

        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer is null)
        {
            this.ScheduleWaiting(now, TimeSpan.FromSeconds(1));
            return;
        }

        var location = DalamudLocationScope.Capture(this.clientState, localPlayer);
        if (location is null)
        {
            this.ScheduleWaiting(now, TimeSpan.FromSeconds(1));
            return;
        }

        var character = DalamudCharacterIdentity.From(localPlayer);
        string? snapshotVersion;
        lock (this.gate)
            snapshotVersion = this.knownSnapshotVersion;

        var request = new SyncRequest(
            this.pluginVersion,
            new ContractCharacterIdentity(character.Name, character.HomeWorldId),
            new ContractLocationScope(
                location.Value.CurrentWorldId,
                location.Value.TerritoryId,
                location.Value.MapId,
                location.Value.InstanceId),
            this.visibilitySelection(),
            snapshotVersion);
        this.StartRequest(serverBaseUri!, currentSettings.InstallationCredential, request, now);
    }

    private void StartRequest(
        Uri serverBaseUri,
        string credential,
        SyncRequest request,
        DateTimeOffset now)
    {
        CancellationTokenSource requestCancellation;
        long requestGeneration;
        lock (this.gate)
        {
            if (this.activeRequest is not null)
                return;

            requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.disposalCancellation.Token);
            this.activeRequest = requestCancellation;
            requestGeneration = ++this.generation;
            this.PublishState(SyncRuntimeStatus.Syncing, now);
        }

        _ = this.ExecuteRequestAsync(
            requestGeneration,
            requestCancellation,
            serverBaseUri,
            credential,
            request);
    }

    private async Task ExecuteRequestAsync(
        long requestGeneration,
        CancellationTokenSource requestCancellation,
        Uri serverBaseUri,
        string credential,
        SyncRequest request)
    {
        try
        {
            var result = await this.apiClient.SyncAsync(
                serverBaseUri,
                credential,
                request,
                requestCancellation.Token).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var listeningChanged = false;
            lock (this.gate)
            {
                if (requestGeneration != this.generation)
                    return;
                if (!this.remotePresence.Apply(result.Response.LocationPresence, request.Location, now))
                {
                    throw new ServerSyncException(
                        "invalid_location_snapshot",
                        "The XIV.fm server returned a snapshot for an unexpected location.");
                }

                this.activeRequest = null;
                this.consecutiveFailures = 0;
                this.knownSnapshotVersion = result.Response.LocationPresence.Version;
                listeningChanged = this.ownListening.Apply(result.Response.OwnListening, now);
                this.nextSyncAt = now.Add(SyncTimingPolicy.FromServerDelay(result.Response.NextSyncAfterSeconds));
                this.lastSuccessAt = now;
                this.lastError = null;
                this.lastErrorCode = null;
                this.PublishState(SyncRuntimeStatus.Healthy, now, this.nextSyncAt, result.RequestId);
            }

            if (listeningChanged)
                this.ownListeningChanged();
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            // Duty entry, logout, location changes, or disposal own the replacement state.
        }
        catch (Exception exception)
        {
            var now = DateTimeOffset.UtcNow;
            lock (this.gate)
            {
                if (requestGeneration != this.generation)
                    return;

                this.activeRequest = null;
                this.consecutiveFailures++;
                this.nextSyncAt = now.Add(SyncTimingPolicy.FailureDelay(this.consecutiveFailures));
                var serverError = exception as ServerSyncException;
                this.lastErrorCode = serverError?.Code;
                this.lastError = serverError is null
                    ? "The XIV.fm server is unavailable."
                    : $"{serverError.Code}: {serverError.Message}";
                this.PublishState(SyncRuntimeStatus.Failed, now, this.nextSyncAt);
            }

            if (exception is ServerSyncException { Code: ConnectionSummary.RelayMembershipRequiredCode })
                Interlocked.Exchange(ref this.relayMembershipRejected, 1);
        }
        finally
        {
            requestCancellation.Dispose();
        }
    }

    private void ScheduleWaiting(DateTimeOffset now, TimeSpan delay)
    {
        lock (this.gate)
        {
            this.nextSyncAt = now.Add(delay);
            this.PublishState(SyncRuntimeStatus.Waiting, now, this.nextSyncAt);
        }
    }

    private void OnWake() => this.RestartAfterLifecycleChange();

    private void OnLogout(int type, int code)
    {
        this.CancelActiveRequest(SyncRuntimeStatus.Waiting, DateTimeOffset.UtcNow);
        this.ownListening.Clear(DateTimeOffset.UtcNow);
    }

    private void OnLocationChanged(uint value) => this.RestartAfterLifecycleChange();

    private void RestartAfterLifecycleChange() => this.CancelActiveRequest(
        SyncRuntimeStatus.Waiting,
        DateTimeOffset.UtcNow);

    private void CancelActiveRequest(SyncRuntimeStatus replacementStatus, DateTimeOffset now)
    {
        lock (this.gate)
        {
            this.generation++;
            this.activeRequest?.Cancel();
            this.activeRequest = null;
            this.nextSyncAt = DateTimeOffset.MinValue;
            this.knownSnapshotVersion = null;
            this.remotePresence.Clear();
            if (replacementStatus == SyncRuntimeStatus.Disabled)
            {
                this.consecutiveFailures = 0;
                this.lastSuccessAt = null;
                this.lastError = null;
                this.lastErrorCode = null;
            }

            this.PublishState(replacementStatus, now);
        }
    }

    /// <summary>
    /// The last outcome stays visible while a replacement request waits or runs. Callers hold the gate.
    /// </summary>
    private void PublishState(
        SyncRuntimeStatus status,
        DateTimeOffset now,
        DateTimeOffset? nextSyncAt = null,
        string? requestId = null) =>
        Volatile.Write(
            ref this.state,
            new SyncRuntimeState(
                status,
                now,
                nextSyncAt,
                requestId,
                this.lastError,
                this.lastErrorCode,
                this.lastSuccessAt));

    public static bool TryValidateSettings(ServerSyncSettings settings, out Uri? serverBaseUri)
    {
        serverBaseUri = null;
        if (!settings.Enabled || string.IsNullOrEmpty(settings.InstallationCredential) || settings.InstallationCredential.Length < 32)
            return false;
        if (string.IsNullOrEmpty(settings.ServerBaseUrl) || !Uri.TryCreate(settings.ServerBaseUrl, UriKind.Absolute, out var candidate))
            return false;
        if (candidate.UserInfo.Length != 0 || candidate.Query.Length != 0 || candidate.Fragment.Length != 0)
            return false;
        var isHttp = string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal);
        var isHttps = string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        if (!isHttps && !(isHttp && candidate.IsLoopback))
            return false;

        serverBaseUri = candidate;
        return true;
    }
}

public sealed record ServerSyncSettings(
    bool Enabled,
    string ServerBaseUrl,
    string InstallationCredential);
