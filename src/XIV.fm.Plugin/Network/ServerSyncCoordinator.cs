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
    private readonly Func<VisibilityMode> visibilityMode;
    private readonly string pluginVersion;
    private readonly CancellationTokenSource disposalCancellation = new();
    private SyncRuntimeState state = SyncRuntimeState.Disabled;
    private ListeningState ownListening = UnavailableListening;
    private CancellationTokenSource? activeRequest;
    private DateTimeOffset nextSyncAt = DateTimeOffset.MinValue;
    private string? knownSnapshotVersion;
    private readonly RemotePresenceStateStore remotePresence = new();
    private int consecutiveFailures;
    private long generation;
    private bool disposed;

    public ServerSyncCoordinator(
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IServerSyncApiClient apiClient,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<ServerSyncSettings> settings,
        Func<VisibilityMode> visibilityMode,
        string pluginVersion)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.apiClient = apiClient;
        this.dutyPolicy = dutyPolicy;
        this.settings = settings;
        this.visibilityMode = visibilityMode;
        this.pluginVersion = pluginVersion;
        this.framework.Update += this.OnFrameworkUpdate;
        this.clientState.Login += this.OnWake;
        this.clientState.Logout += this.OnLogout;
        this.clientState.TerritoryChanged += this.OnLocationChanged;
        this.clientState.MapIdChanged += this.OnLocationChanged;
        this.clientState.InstanceChanged += this.OnLocationChanged;
    }

    private static ListeningState UnavailableListening { get; } =
        new(ListeningStatus.Unavailable, false, null, null);

    public SyncRuntimeState State => Volatile.Read(ref this.state);

    public ListeningState OwnListening => Volatile.Read(ref this.ownListening);

    public ImmutableArray<OverlayCard> GetRemoteCards(DateTimeOffset now) =>
        this.remotePresence.Read(now);

    public void RequestImmediateSync() => this.RestartAfterLifecycleChange();

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
        this.CancelActiveRequest(SyncRuntimeState.Disabled);
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
                this.CancelActiveRequest(new SyncRuntimeState(
                    SyncRuntimeStatus.SuspendedDuty,
                    now));
            }

            return;
        }

        var currentSettings = this.settings();
        if (!TryValidateSettings(currentSettings, out var serverBaseUri))
        {
            if (this.State.Status != SyncRuntimeStatus.Disabled)
                this.CancelActiveRequest(SyncRuntimeState.Disabled with { UpdatedAt = now });

            return;
        }

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
            new VisibilitySelection(this.visibilityMode(), []),
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
            Volatile.Write(ref this.state, new SyncRuntimeState(SyncRuntimeStatus.Syncing, now));
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
                Volatile.Write(ref this.ownListening, result.Response.OwnListening);
                this.nextSyncAt = now.Add(SyncTimingPolicy.FromServerDelay(result.Response.NextSyncAfterSeconds));
                Volatile.Write(
                    ref this.state,
                    new SyncRuntimeState(
                        SyncRuntimeStatus.Healthy,
                        now,
                        this.nextSyncAt,
                        result.RequestId));
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            // Duty entry, logout, location changes, or disposal own the replacement state.
        }
        catch (Exception exception) when (exception is ServerSyncException or HttpRequestException or TaskCanceledException)
        {
            var now = DateTimeOffset.UtcNow;
            lock (this.gate)
            {
                if (requestGeneration != this.generation)
                    return;

                this.activeRequest = null;
                this.consecutiveFailures++;
                this.nextSyncAt = now.Add(SyncTimingPolicy.FailureDelay(this.consecutiveFailures));
                var error = exception is ServerSyncException serverError
                    ? $"{serverError.Code}: {serverError.Message}"
                    : "The development server is unavailable.";
                Volatile.Write(
                    ref this.state,
                    new SyncRuntimeState(
                        SyncRuntimeStatus.Failed,
                        now,
                        this.nextSyncAt,
                        Error: error));
            }
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
            Volatile.Write(
                ref this.state,
                new SyncRuntimeState(SyncRuntimeStatus.Waiting, now, this.nextSyncAt));
        }
    }

    private void OnWake() => this.RestartAfterLifecycleChange();

    private void OnLogout(int type, int code) => this.CancelActiveRequest(
        new SyncRuntimeState(SyncRuntimeStatus.Waiting, DateTimeOffset.UtcNow));

    private void OnLocationChanged(uint value) => this.RestartAfterLifecycleChange();

    private void RestartAfterLifecycleChange() => this.CancelActiveRequest(
        new SyncRuntimeState(SyncRuntimeStatus.Waiting, DateTimeOffset.UtcNow));

    private void CancelActiveRequest(SyncRuntimeState replacementState)
    {
        lock (this.gate)
        {
            this.generation++;
            this.activeRequest?.Cancel();
            this.activeRequest = null;
            this.nextSyncAt = DateTimeOffset.MinValue;
            this.knownSnapshotVersion = null;
            this.remotePresence.Clear();
            Volatile.Write(ref this.state, replacementState);
        }
    }

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
