using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Policy;

namespace XIV.fm.Plugin.Network;

public enum DeviceRuntimeStatus
{
    Idle,
    Loading,
    Ready,
    Failed,
}

public sealed record DeviceRuntimeState(
    DeviceRuntimeStatus Status,
    ImmutableArray<InstallationResponse> Devices,
    string? Error = null)
{
    public static DeviceRuntimeState Empty { get; } = new(DeviceRuntimeStatus.Idle, []);
}

/// <summary>
/// Loads and disconnects the plugin installations linked to the same Last.fm account.
/// </summary>
public sealed class DeviceCoordinator : IDisposable
{
    private readonly Lock gate = new();
    private readonly IFramework framework;
    private readonly IInstallationApiClient apiClient;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<ServerSyncSettings> settings;
    private readonly string pluginVersion;
    private readonly ConcurrentQueue<Action> frameworkActions = new();
    private readonly CancellationTokenSource disposalCancellation = new();
    private DeviceRuntimeState state = DeviceRuntimeState.Empty;
    private CancellationTokenSource? activeRequest;
    private long generation;
    private bool disposed;

    public DeviceCoordinator(
        IFramework framework,
        IInstallationApiClient apiClient,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<ServerSyncSettings> settings,
        string pluginVersion)
    {
        this.framework = framework;
        this.apiClient = apiClient;
        this.dutyPolicy = dutyPolicy;
        this.settings = settings;
        this.pluginVersion = pluginVersion;
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public DeviceRuntimeState State => Volatile.Read(ref this.state);

    public void Refresh() => this.Start(static (_, _, _) => Task.CompletedTask);

    public void Disconnect(Guid installationId) => this.Start((server, credential, cancellationToken) =>
        this.apiClient.RevokeInstallationAsync(
            server,
            credential,
            this.pluginVersion,
            installationId,
            cancellationToken));

    public void Reset()
    {
        this.Cancel();
        Volatile.Write(ref this.state, DeviceRuntimeState.Empty);
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.framework.Update -= this.OnFrameworkUpdate;
        this.disposalCancellation.Cancel();
        this.Cancel();
        this.disposalCancellation.Dispose();
        if (this.apiClient is IDisposable disposable)
            disposable.Dispose();
    }

    private void Start(Func<Uri, string, CancellationToken, Task> operation)
    {
        var current = this.settings();
        if (!this.dutyPolicy().AllowsServerRequests ||
            !ServerSyncCoordinator.TryValidateSettings(current, out var serverBaseUri))
        {
            return;
        }

        lock (this.gate)
        {
            if (this.activeRequest is not null)
                return;

            this.activeRequest = CancellationTokenSource.CreateLinkedTokenSource(this.disposalCancellation.Token);
            var requestGeneration = ++this.generation;
            Volatile.Write(ref this.state, this.State with { Status = DeviceRuntimeStatus.Loading, Error = null });
            _ = this.ExecuteAsync(
                serverBaseUri!,
                current.InstallationCredential,
                operation,
                this.activeRequest,
                requestGeneration);
        }
    }

    private async Task ExecuteAsync(
        Uri serverBaseUri,
        string credential,
        Func<Uri, string, CancellationToken, Task> operation,
        CancellationTokenSource requestCancellation,
        long requestGeneration)
    {
        DeviceRuntimeState result;
        try
        {
            await operation(serverBaseUri, credential, requestCancellation.Token).ConfigureAwait(false);
            var response = await this.apiClient.ListInstallationsAsync(
                serverBaseUri,
                credential,
                this.pluginVersion,
                requestCancellation.Token).ConfigureAwait(false);
            result = new DeviceRuntimeState(DeviceRuntimeStatus.Ready, response.Installations.ToImmutableArray());
        }
        catch (Exception exception)
        {
            result = this.State with
            {
                Status = DeviceRuntimeStatus.Failed,
                Error = exception is ServerSyncException serverError
                    ? serverError.Message
                    : "The XIV.fm server is unavailable.",
            };
        }

        this.frameworkActions.Enqueue(() =>
        {
            lock (this.gate)
            {
                if (requestGeneration == this.generation)
                {
                    this.activeRequest = null;
                    Volatile.Write(ref this.state, result);
                }
            }

            requestCancellation.Dispose();
        });
    }

    private void Cancel()
    {
        lock (this.gate)
        {
            this.generation++;
            this.activeRequest?.Cancel();
            this.activeRequest = null;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.dutyPolicy().AllowsServerRequests && this.State.Status == DeviceRuntimeStatus.Loading)
        {
            this.Cancel();
            Volatile.Write(ref this.state, this.State with { Status = DeviceRuntimeStatus.Idle });
        }

        while (this.frameworkActions.TryDequeue(out var action))
            action();
    }
}
