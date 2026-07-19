using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Policy;

namespace XIV.fm.Plugin.Network;

public enum AccountLinkRuntimeStatus
{
    Unlinked,
    Starting,
    WaitingForBrowser,
    Linked,
    Failed,
    SuspendedDuty,
}

public sealed record AccountLinkRuntimeState(
    AccountLinkRuntimeStatus Status,
    DateTimeOffset UpdatedAt,
    string? AccountName = null,
    string? Error = null);

public sealed record PendingAccountLink(Guid SessionId, string Credential, DateTimeOffset ExpiresAt);

public sealed record AccountLinkSettings(Uri ServerBaseUri, PendingAccountLink? PendingLink);

public sealed class AccountLinkCoordinator : IDisposable
{
    private readonly Lock gate = new();
    private readonly IFramework framework;
    private readonly IAccountLinkApiClient apiClient;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<AccountLinkSettings> settings;
    private readonly Action<PendingAccountLink> savePending;
    private readonly Action<string, string> completeLink;
    private readonly Action clearPending;
    private readonly Action<Uri> openBrowser;
    private readonly string pluginVersion;
    private readonly ConcurrentQueue<Action> frameworkActions = new();
    private readonly CancellationTokenSource disposalCancellation = new();
    private AccountLinkRuntimeState state = new(AccountLinkRuntimeStatus.Unlinked, DateTimeOffset.MinValue);
    private CancellationTokenSource? activeRequest;
    private DateTimeOffset nextPollAt = DateTimeOffset.MinValue;
    private long generation;
    private bool disposed;

    public AccountLinkCoordinator(
        IFramework framework,
        IAccountLinkApiClient apiClient,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<AccountLinkSettings> settings,
        Action<PendingAccountLink> savePending,
        Action<string, string> completeLink,
        Action clearPending,
        Action<Uri> openBrowser,
        string pluginVersion)
    {
        this.framework = framework;
        this.apiClient = apiClient;
        this.dutyPolicy = dutyPolicy;
        this.settings = settings;
        this.savePending = savePending;
        this.completeLink = completeLink;
        this.clearPending = clearPending;
        this.openBrowser = openBrowser;
        this.pluginVersion = pluginVersion;
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public AccountLinkRuntimeState State => Volatile.Read(ref this.state);

    public bool TryStart(out string? error)
    {
        error = null;
        if (!this.dutyPolicy().AllowsServerRequests)
        {
            error = "Account linking is suspended while bound by duty.";
            return false;
        }

        var current = this.settings();
        if (current.PendingLink is not null && current.PendingLink.ExpiresAt > DateTimeOffset.UtcNow)
        {
            error = "An account-link session is already pending.";
            return false;
        }

        lock (this.gate)
        {
            if (this.activeRequest is not null)
            {
                error = "An account-link request is already running.";
                return false;
            }

            this.activeRequest = CancellationTokenSource.CreateLinkedTokenSource(this.disposalCancellation.Token);
            var requestGeneration = ++this.generation;
            Volatile.Write(
                ref this.state,
                new AccountLinkRuntimeState(AccountLinkRuntimeStatus.Starting, DateTimeOffset.UtcNow));
            _ = this.StartAsync(current.ServerBaseUri, this.activeRequest, requestGeneration);
            return true;
        }
    }

    public void CancelPending()
    {
        lock (this.gate)
        {
            this.generation++;
            this.activeRequest?.Cancel();
            this.activeRequest = null;
            this.nextPollAt = DateTimeOffset.MinValue;
        }

        this.clearPending();
        Volatile.Write(
            ref this.state,
            new AccountLinkRuntimeState(AccountLinkRuntimeStatus.Unlinked, DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.framework.Update -= this.OnFrameworkUpdate;
        this.disposalCancellation.Cancel();
        lock (this.gate)
        {
            this.activeRequest?.Cancel();
            this.activeRequest = null;
        }

        this.disposalCancellation.Dispose();
        if (this.apiClient is IDisposable disposable)
            disposable.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTimeOffset.UtcNow;
        if (!this.dutyPolicy().AllowsServerRequests)
        {
            lock (this.gate)
            {
                this.activeRequest?.Cancel();
                this.activeRequest = null;
            }

            Volatile.Write(
                ref this.state,
                new AccountLinkRuntimeState(AccountLinkRuntimeStatus.SuspendedDuty, now));
            return;
        }

        while (this.frameworkActions.TryDequeue(out var action))
            action();

        var current = this.settings();
        var pending = current.PendingLink;
        if (pending is null)
            return;
        if (pending.ExpiresAt <= now)
        {
            this.clearPending();
            Volatile.Write(
                ref this.state,
                new AccountLinkRuntimeState(
                    AccountLinkRuntimeStatus.Failed,
                    now,
                    Error: "The account-link session expired."));
            return;
        }

        lock (this.gate)
        {
            if (this.activeRequest is not null || now < this.nextPollAt)
                return;

            this.activeRequest = CancellationTokenSource.CreateLinkedTokenSource(this.disposalCancellation.Token);
            _ = this.PollAsync(current.ServerBaseUri, pending, this.activeRequest, this.generation);
        }
    }

    private async Task StartAsync(
        Uri serverBaseUri,
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

            var started = await this.apiClient.StartAccountLinkAsync(
                serverBaseUri,
                this.pluginVersion,
                requestCancellation.Token).ConfigureAwait(false);
            this.frameworkActions.Enqueue(() =>
            {
                if (!this.IsCurrentGeneration(requestGeneration))
                {
                    this.CompleteActiveRequest(requestCancellation);
                    return;
                }

                this.CompleteActiveRequest(requestCancellation);
                if (!this.dutyPolicy().AllowsServerRequests)
                {
                    Volatile.Write(
                        ref this.state,
                        new AccountLinkRuntimeState(
                            AccountLinkRuntimeStatus.SuspendedDuty,
                            DateTimeOffset.UtcNow));
                    return;
                }

                var pending = new PendingAccountLink(
                    started.LinkSessionId,
                    started.LinkCredential,
                    started.ExpiresAt);
                this.savePending(pending);
                this.nextPollAt = DateTimeOffset.UtcNow.AddSeconds(started.PollAfterSeconds);
                Volatile.Write(
                    ref this.state,
                    new AccountLinkRuntimeState(
                        AccountLinkRuntimeStatus.WaitingForBrowser,
                        DateTimeOffset.UtcNow));
                this.openBrowser(started.AuthorizationUrl);
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            requestCancellation.Dispose();
        }
        catch (Exception exception) when (exception is ServerSyncException or HttpRequestException or TaskCanceledException)
        {
            this.QueueFailure(requestCancellation, requestGeneration, exception);
        }
    }

    private async Task PollAsync(
        Uri serverBaseUri,
        PendingAccountLink pending,
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

            var result = await this.apiClient.GetAccountLinkStatusAsync(
                serverBaseUri,
                pending.SessionId,
                pending.Credential,
                this.pluginVersion,
                requestCancellation.Token).ConfigureAwait(false);
            this.frameworkActions.Enqueue(() =>
            {
                if (!this.IsCurrentGeneration(requestGeneration))
                {
                    this.CompleteActiveRequest(requestCancellation);
                    return;
                }

                this.CompleteActiveRequest(requestCancellation);
                switch (result.Status)
                {
                    case AccountLinkStatus.Linked when !string.IsNullOrWhiteSpace(result.LastFmAccountName):
                        this.completeLink(pending.Credential, result.LastFmAccountName);
                        Volatile.Write(
                            ref this.state,
                            new AccountLinkRuntimeState(
                                AccountLinkRuntimeStatus.Linked,
                                DateTimeOffset.UtcNow,
                                result.LastFmAccountName));
                        break;
                    case AccountLinkStatus.Pending:
                        this.nextPollAt = DateTimeOffset.UtcNow.AddSeconds(2);
                        Volatile.Write(
                            ref this.state,
                            new AccountLinkRuntimeState(
                                AccountLinkRuntimeStatus.WaitingForBrowser,
                                DateTimeOffset.UtcNow));
                        break;
                    default:
                        this.clearPending();
                        Volatile.Write(
                            ref this.state,
                            new AccountLinkRuntimeState(
                                AccountLinkRuntimeStatus.Failed,
                                DateTimeOffset.UtcNow,
                                Error: $"Account linking {result.Status.ToString().ToLowerInvariant()}."));
                        break;
                }
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            requestCancellation.Dispose();
        }
        catch (Exception exception) when (exception is ServerSyncException or HttpRequestException or TaskCanceledException)
        {
            this.QueueFailure(requestCancellation, requestGeneration, exception);
        }
    }

    private void QueueFailure(
        CancellationTokenSource requestCancellation,
        long requestGeneration,
        Exception exception) =>
        this.frameworkActions.Enqueue(() =>
        {
            if (!this.IsCurrentGeneration(requestGeneration))
            {
                this.CompleteActiveRequest(requestCancellation);
                return;
            }

            this.CompleteActiveRequest(requestCancellation);
            this.nextPollAt = DateTimeOffset.UtcNow.AddSeconds(15);
            var error = exception is ServerSyncException serverError
                ? $"{serverError.Code}: {serverError.Message}"
                : "The XIV.fm server is unavailable.";
            Volatile.Write(
                ref this.state,
                new AccountLinkRuntimeState(
                    AccountLinkRuntimeStatus.Failed,
                    DateTimeOffset.UtcNow,
                    Error: error));
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
