using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Core.Policy;

namespace XIV.fm.Plugin.Network;

public enum AccountDisconnectRuntimeStatus
{
    Idle,
    Disconnecting,
    Disconnected,
    Failed,
    SuspendedDuty,
}

public sealed record AccountDisconnectRuntimeState(
    AccountDisconnectRuntimeStatus Status,
    DateTimeOffset UpdatedAt,
    string? Error = null);

public sealed class AccountDisconnectCoordinator : IDisposable
{
    private readonly Lock gate = new();
    private readonly IFramework framework;
    private readonly IInstallationApiClient apiClient;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<ServerSyncSettings> settings;
    private readonly Action completeDisconnect;
    private readonly string pluginVersion;
    private readonly ConcurrentQueue<Action> frameworkActions = new();
    private readonly CancellationTokenSource disposalCancellation = new();
    private AccountDisconnectRuntimeState state = new(AccountDisconnectRuntimeStatus.Idle, DateTimeOffset.MinValue);
    private CancellationTokenSource? activeRequest;
    private long generation;
    private bool disposed;

    public AccountDisconnectCoordinator(
        IFramework framework,
        IInstallationApiClient apiClient,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<ServerSyncSettings> settings,
        Action completeDisconnect,
        string pluginVersion)
    {
        this.framework = framework;
        this.apiClient = apiClient;
        this.dutyPolicy = dutyPolicy;
        this.settings = settings;
        this.completeDisconnect = completeDisconnect;
        this.pluginVersion = pluginVersion;
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public AccountDisconnectRuntimeState State => Volatile.Read(ref this.state);

    public bool TryStart(out string? error)
    {
        error = null;
        if (!this.dutyPolicy().AllowsServerRequests)
        {
            error = "Account disconnection is suspended while bound by duty.";
            Volatile.Write(
                ref this.state,
                new AccountDisconnectRuntimeState(
                    AccountDisconnectRuntimeStatus.SuspendedDuty,
                    DateTimeOffset.UtcNow));
            return false;
        }

        var current = this.settings();
        if (!ServerSyncCoordinator.TryValidateSettings(current, out var serverBaseUri))
        {
            error = "No linked XIV.fm installation is available.";
            return false;
        }

        lock (this.gate)
        {
            if (this.activeRequest is not null)
            {
                error = "Account disconnection is already running.";
                return false;
            }

            this.activeRequest = CancellationTokenSource.CreateLinkedTokenSource(this.disposalCancellation.Token);
            var requestGeneration = ++this.generation;
            Volatile.Write(
                ref this.state,
                new AccountDisconnectRuntimeState(
                    AccountDisconnectRuntimeStatus.Disconnecting,
                    DateTimeOffset.UtcNow));
            _ = this.DisconnectAsync(
                serverBaseUri!,
                current.InstallationCredential,
                this.activeRequest,
                requestGeneration);
            return true;
        }
    }

    public void Reset() => Volatile.Write(
        ref this.state,
        new AccountDisconnectRuntimeState(AccountDisconnectRuntimeStatus.Idle, DateTimeOffset.UtcNow));

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.framework.Update -= this.OnFrameworkUpdate;
        this.disposalCancellation.Cancel();
        lock (this.gate)
        {
            this.generation++;
            this.activeRequest?.Cancel();
            this.activeRequest = null;
        }

        this.disposalCancellation.Dispose();
        if (this.apiClient is IDisposable disposable)
            disposable.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.dutyPolicy().AllowsServerRequests)
        {
            lock (this.gate)
            {
                if (this.activeRequest is not null)
                {
                    this.generation++;
                    this.activeRequest.Cancel();
                    this.activeRequest = null;
                }
            }

            if (this.State.Status == AccountDisconnectRuntimeStatus.Disconnecting)
            {
                Volatile.Write(
                    ref this.state,
                    new AccountDisconnectRuntimeState(
                        AccountDisconnectRuntimeStatus.SuspendedDuty,
                        DateTimeOffset.UtcNow));
            }

            return;
        }

        while (this.frameworkActions.TryDequeue(out var action))
            action();
    }

    private async Task DisconnectAsync(
        Uri serverBaseUri,
        string credential,
        CancellationTokenSource requestCancellation,
        long requestGeneration)
    {
        try
        {
            if (!this.dutyPolicy().AllowsServerRequests)
            {
                requestCancellation.Cancel();
                throw new OperationCanceledException(requestCancellation.Token);
            }

            await this.apiClient.RevokeCurrentInstallationAsync(
                serverBaseUri,
                credential,
                this.pluginVersion,
                requestCancellation.Token).ConfigureAwait(false);
            this.QueueCompletion(requestCancellation, requestGeneration);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            requestCancellation.Dispose();
        }
        catch (ServerSyncException exception) when (exception.Code == "installation_credential_required")
        {
            this.QueueCompletion(requestCancellation, requestGeneration);
        }
        catch (Exception exception) when (exception is ServerSyncException or HttpRequestException or TaskCanceledException)
        {
            this.frameworkActions.Enqueue(() =>
            {
                if (!this.IsCurrentGeneration(requestGeneration))
                {
                    this.CompleteActiveRequest(requestCancellation);
                    return;
                }

                this.CompleteActiveRequest(requestCancellation);
                var error = exception is ServerSyncException serverError
                    ? $"{serverError.Code}: {serverError.Message}"
                    : "The XIV.fm server is unavailable.";
                Volatile.Write(
                    ref this.state,
                    new AccountDisconnectRuntimeState(
                        AccountDisconnectRuntimeStatus.Failed,
                        DateTimeOffset.UtcNow,
                        error));
            });
        }
    }

    private void QueueCompletion(
        CancellationTokenSource requestCancellation,
        long requestGeneration) =>
        this.frameworkActions.Enqueue(() =>
        {
            if (!this.IsCurrentGeneration(requestGeneration))
            {
                this.CompleteActiveRequest(requestCancellation);
                return;
            }

            this.CompleteActiveRequest(requestCancellation);
            this.completeDisconnect();
            Volatile.Write(
                ref this.state,
                new AccountDisconnectRuntimeState(
                    AccountDisconnectRuntimeStatus.Disconnected,
                    DateTimeOffset.UtcNow));
        });

    private bool IsCurrentGeneration(long requestGeneration)
    {
        lock (this.gate)
            return requestGeneration == this.generation;
    }

    private void CompleteActiveRequest(CancellationTokenSource requestCancellation)
    {
        lock (this.gate)
        {
            if (ReferenceEquals(this.activeRequest, requestCancellation))
                this.activeRequest = null;
        }

        requestCancellation.Dispose();
    }
}
