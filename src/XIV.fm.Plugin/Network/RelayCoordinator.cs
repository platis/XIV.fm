using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Policy;

namespace XIV.fm.Plugin.Network;

public enum RelayRuntimeStatus
{
    Idle,
    Loading,
    Working,
    Ready,
    Failed,
    SuspendedDuty,
}

public sealed record RelayRuntimeState(
    RelayRuntimeStatus Status,
    DateTimeOffset UpdatedAt,
    ImmutableArray<RelayResponse> Relays,
    Guid? ManagedRelayId = null,
    ImmutableArray<RelayMemberResponse> Members = default,
    ImmutableArray<RelayInvitationResponse> Invitations = default,
    RelayInvitationPreviewResponse? InvitationPreview = null,
    string? CreatedInvitationToken = null,
    string? Message = null,
    string? Error = null)
{
    public static RelayRuntimeState Empty { get; } = new(
        RelayRuntimeStatus.Idle,
        DateTimeOffset.MinValue,
        []);
}

public sealed class RelayCoordinator : IDisposable
{
    private readonly Lock gate = new();
    private readonly IFramework framework;
    private readonly IRelayApiClient apiClient;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<ServerSyncSettings> settings;
    private readonly Action<IReadOnlyCollection<Guid>, Guid?> membershipsRefreshed;
    private readonly string pluginVersion;
    private readonly ConcurrentQueue<Action> frameworkActions = new();
    private readonly CancellationTokenSource disposalCancellation = new();
    private RelayRuntimeState state = RelayRuntimeState.Empty;
    private CancellationTokenSource? activeRequest;
    private long generation;
    private bool disposed;

    public RelayCoordinator(
        IFramework framework,
        IRelayApiClient apiClient,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<ServerSyncSettings> settings,
        Action<IReadOnlyCollection<Guid>, Guid?> membershipsRefreshed,
        string pluginVersion)
    {
        this.framework = framework;
        this.apiClient = apiClient;
        this.dutyPolicy = dutyPolicy;
        this.settings = settings;
        this.membershipsRefreshed = membershipsRefreshed;
        this.pluginVersion = pluginVersion;
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public RelayRuntimeState State => Volatile.Read(ref this.state);

    public void EnsureLoaded()
    {
        if (this.State.Status == RelayRuntimeStatus.Idle)
            this.TryRefresh(out _);
    }

    public bool TryRefresh(out string? error) => this.TryStart(
        "Loading your Custom Relays…",
        async (context, cancellationToken) =>
        {
            var relays = await this.apiClient.ListRelaysAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                cancellationToken).ConfigureAwait(false);
            return new RelayOperationCompletion(
                relays.Relays.ToImmutableArray(),
                MembershipListIsAuthoritative: true,
                Message: "Relays are up to date.");
        },
        RelayRuntimeStatus.Loading,
        out error);

    public bool TryCreate(string name, out string? error)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length < 3 || normalizedName.Length > 48)
        {
            error = "Relay names must be between 3 and 48 characters.";
            return false;
        }

        return this.TryStart(
            "Creating the Relay…",
            async (context, cancellationToken) =>
            {
                var created = await this.apiClient.CreateRelayAsync(
                    context.ServerBaseUri,
                    context.Credential,
                    this.pluginVersion,
                    new CreateRelayRequest(normalizedName, Guid.NewGuid()),
                    cancellationToken).ConfigureAwait(false);
                var relays = await this.ListAsync(context, cancellationToken).ConfigureAwait(false);
                return new RelayOperationCompletion(
                    relays,
                    AutoSelectedRelayId: created.RelayId,
                    MembershipListIsAuthoritative: true,
                    Message: $"Created {created.Name} and selected it in Privacy.");
            },
            RelayRuntimeStatus.Working,
            out error);
    }

    public bool TryPreviewInvitation(string token, out string? error)
    {
        var normalizedToken = token.Trim();
        if (normalizedToken.Length < 32 || normalizedToken.Length > 512)
        {
            error = "Enter a complete Relay invitation token.";
            return false;
        }

        return this.TryStart(
            "Checking the invitation…",
            async (context, cancellationToken) =>
            {
                var preview = await this.apiClient.PreviewRelayInvitationAsync(
                    context.ServerBaseUri,
                    context.Credential,
                    this.pluginVersion,
                    new RelayInvitationTokenRequest(normalizedToken),
                    cancellationToken).ConfigureAwait(false);
                return new RelayOperationCompletion(
                    this.State.Relays,
                    InvitationPreview: preview,
                    Message: $"Invitation found for {preview.RelayName}.");
            },
            RelayRuntimeStatus.Working,
            out error);
    }

    public bool TryAcceptInvitation(string token, out string? error)
    {
        var normalizedToken = token.Trim();
        if (normalizedToken.Length < 32 || normalizedToken.Length > 512)
        {
            error = "Enter a complete Relay invitation token.";
            return false;
        }

        return this.TryStart(
            "Joining the Relay…",
            async (context, cancellationToken) =>
            {
                var accepted = await this.apiClient.AcceptRelayInvitationAsync(
                    context.ServerBaseUri,
                    context.Credential,
                    this.pluginVersion,
                    new RelayInvitationTokenRequest(normalizedToken),
                    cancellationToken).ConfigureAwait(false);
                var relays = await this.ListAsync(context, cancellationToken).ConfigureAwait(false);
                return new RelayOperationCompletion(
                    relays,
                    AutoSelectedRelayId: accepted.Relay.RelayId,
                    MembershipListIsAuthoritative: true,
                    Message: $"Joined {accepted.Relay.Name} and selected it in Privacy.");
            },
            RelayRuntimeStatus.Working,
            out error);
    }

    public bool TryLoadManagement(Guid relayId, out string? error) => this.TryStart(
        "Loading Relay management…",
        async (context, cancellationToken) =>
        {
            var membersTask = this.apiClient.ListRelayMembersAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                cancellationToken);
            var invitationsTask = this.apiClient.ListRelayInvitationsAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                cancellationToken);
            await Task.WhenAll(membersTask, invitationsTask).ConfigureAwait(false);
            return new RelayOperationCompletion(
                this.State.Relays,
                relayId,
                (await membersTask.ConfigureAwait(false)).Members.ToImmutableArray(),
                (await invitationsTask.ConfigureAwait(false)).Invitations.ToImmutableArray(),
                Message: "Relay management is up to date.");
        },
        RelayRuntimeStatus.Loading,
        out error);

    public bool TryRename(Guid relayId, string name, out string? error)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length < 3 || normalizedName.Length > 48)
        {
            error = "Relay names must be between 3 and 48 characters.";
            return false;
        }

        return this.TryStart(
            "Renaming the Relay…",
            async (context, cancellationToken) =>
            {
                var renamed = await this.apiClient.RenameRelayAsync(
                    context.ServerBaseUri,
                    context.Credential,
                    this.pluginVersion,
                    relayId,
                    new RenameRelayRequest(normalizedName),
                    cancellationToken).ConfigureAwait(false);
                var relays = await this.ListAsync(context, cancellationToken).ConfigureAwait(false);
                return new RelayOperationCompletion(
                    relays,
                    relayId,
                    this.State.Members,
                    this.State.Invitations,
                    MembershipListIsAuthoritative: true,
                    Message: $"Renamed the Relay to {renamed.Name}.");
            },
            RelayRuntimeStatus.Working,
            out error);
    }

    public bool TryCreateInvitation(Guid relayId, out string? error) => this.TryStart(
        "Creating a single-use invitation…",
        async (context, cancellationToken) =>
        {
            var created = await this.apiClient.CreateRelayInvitationAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                new CreateRelayInvitationRequest(),
                cancellationToken).ConfigureAwait(false);
            var invitations = await this.apiClient.ListRelayInvitationsAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                cancellationToken).ConfigureAwait(false);
            return new RelayOperationCompletion(
                this.State.Relays,
                relayId,
                this.State.Members,
                invitations.Invitations.ToImmutableArray(),
                CreatedInvitationToken: created.Token,
                Message: "Invitation created. Copy it now; XIV.fm cannot show it again.");
        },
        RelayRuntimeStatus.Working,
        out error);

    public bool TryRevokeInvitation(Guid relayId, Guid invitationId, out string? error) => this.TryStart(
        "Revoking the invitation…",
        async (context, cancellationToken) =>
        {
            await this.apiClient.RevokeRelayInvitationAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                invitationId,
                cancellationToken).ConfigureAwait(false);
            var invitations = await this.apiClient.ListRelayInvitationsAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                cancellationToken).ConfigureAwait(false);
            return new RelayOperationCompletion(
                this.State.Relays,
                relayId,
                this.State.Members,
                invitations.Invitations.ToImmutableArray(),
                Message: "Invitation revoked.");
        },
        RelayRuntimeStatus.Working,
        out error);

    public bool TryKickMember(Guid relayId, Guid membershipId, out string? error) => this.TryStart(
        "Removing the member…",
        async (context, cancellationToken) =>
        {
            await this.apiClient.KickRelayMemberAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                membershipId,
                cancellationToken).ConfigureAwait(false);
            var members = await this.apiClient.ListRelayMembersAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                cancellationToken).ConfigureAwait(false);
            var relays = await this.ListAsync(context, cancellationToken).ConfigureAwait(false);
            return new RelayOperationCompletion(
                relays,
                relayId,
                members.Members.ToImmutableArray(),
                this.State.Invitations,
                MembershipListIsAuthoritative: true,
                Message: "Member removed.");
        },
        RelayRuntimeStatus.Working,
        out error);

    public bool TryLeave(Guid relayId, out string? error) => this.TryStart(
        "Leaving the Relay…",
        async (context, cancellationToken) =>
        {
            await this.apiClient.LeaveRelayAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                cancellationToken).ConfigureAwait(false);
            var relays = await this.ListAsync(context, cancellationToken).ConfigureAwait(false);
            return new RelayOperationCompletion(
                relays,
                MembershipListIsAuthoritative: true,
                Message: "You left the Relay.");
        },
        RelayRuntimeStatus.Working,
        out error);

    public bool TryDelete(Guid relayId, out string? error) => this.TryStart(
        "Deleting the Relay…",
        async (context, cancellationToken) =>
        {
            await this.apiClient.DeleteRelayAsync(
                context.ServerBaseUri,
                context.Credential,
                this.pluginVersion,
                relayId,
                cancellationToken).ConfigureAwait(false);
            var relays = await this.ListAsync(context, cancellationToken).ConfigureAwait(false);
            return new RelayOperationCompletion(
                relays,
                MembershipListIsAuthoritative: true,
                Message: "Relay deleted.");
        },
        RelayRuntimeStatus.Working,
        out error);

    public void ClearInvitationSecret()
    {
        var current = this.State;
        Volatile.Write(ref this.state, current with { CreatedInvitationToken = null });
    }

    public void Reset()
    {
        lock (this.gate)
        {
            this.generation++;
            this.activeRequest?.Cancel();
            this.activeRequest = null;
        }

        Volatile.Write(ref this.state, RelayRuntimeState.Empty);
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
            this.generation++;
            this.activeRequest?.Cancel();
            this.activeRequest = null;
        }

        this.disposalCancellation.Dispose();
        if (this.apiClient is IDisposable disposable)
            disposable.Dispose();
    }

    private bool TryStart(
        string progressMessage,
        Func<RelayApiContext, CancellationToken, Task<RelayOperationCompletion>> operation,
        RelayRuntimeStatus status,
        out string? error)
    {
        error = null;
        if (!this.dutyPolicy().AllowsServerRequests)
        {
            error = "Custom Relay actions are unavailable while bound by duty.";
            return false;
        }

        var currentSettings = this.settings();
        if (!ServerSyncCoordinator.TryValidateSettings(currentSettings, out var serverBaseUri))
        {
            error = "Connect Last.fm before using Custom Relays.";
            return false;
        }

        lock (this.gate)
        {
            if (this.activeRequest is not null)
            {
                error = "Another Custom Relay action is still running.";
                return false;
            }

            this.activeRequest = CancellationTokenSource.CreateLinkedTokenSource(this.disposalCancellation.Token);
            var requestGeneration = ++this.generation;
            var context = new RelayApiContext(serverBaseUri!, currentSettings.InstallationCredential);
            Volatile.Write(
                ref this.state,
                this.State with
                {
                    Status = status,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    InvitationPreview = null,
                    Message = progressMessage,
                    Error = null,
                });
            _ = this.ExecuteAsync(context, operation, this.activeRequest, requestGeneration);
            return true;
        }
    }

    private async Task ExecuteAsync(
        RelayApiContext context,
        Func<RelayApiContext, CancellationToken, Task<RelayOperationCompletion>> operation,
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

            var completion = await operation(context, requestCancellation.Token).ConfigureAwait(false);
            this.frameworkActions.Enqueue(() => this.CompleteOperation(
                requestCancellation,
                requestGeneration,
                completion));
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            requestCancellation.Dispose();
        }
        catch (Exception exception) when (exception is ServerSyncException or HttpRequestException or TaskCanceledException)
        {
            this.frameworkActions.Enqueue(() => this.FailOperation(
                requestCancellation,
                requestGeneration,
                exception));
        }
    }

    private void CompleteOperation(
        CancellationTokenSource requestCancellation,
        long requestGeneration,
        RelayOperationCompletion completion)
    {
        if (!this.IsCurrentGeneration(requestGeneration))
        {
            this.CompleteActiveRequest(requestCancellation);
            return;
        }

        this.CompleteActiveRequest(requestCancellation);
        Volatile.Write(
            ref this.state,
            new RelayRuntimeState(
                RelayRuntimeStatus.Ready,
                DateTimeOffset.UtcNow,
                completion.Relays,
                completion.ManagedRelayId,
                completion.Members.IsDefault ? [] : completion.Members,
                completion.Invitations.IsDefault ? [] : completion.Invitations,
                completion.InvitationPreview,
                completion.CreatedInvitationToken,
                completion.Message));
        if (completion.MembershipListIsAuthoritative)
        {
            this.membershipsRefreshed(
                completion.Relays.Select(relay => relay.RelayId).ToArray(),
                completion.AutoSelectedRelayId);
        }
    }

    private void FailOperation(
        CancellationTokenSource requestCancellation,
        long requestGeneration,
        Exception exception)
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
            this.State with
            {
                Status = RelayRuntimeStatus.Failed,
                UpdatedAt = DateTimeOffset.UtcNow,
                Error = error,
                Message = null,
            });
    }

    private async Task<ImmutableArray<RelayResponse>> ListAsync(
        RelayApiContext context,
        CancellationToken cancellationToken)
    {
        var response = await this.apiClient.ListRelaysAsync(
            context.ServerBaseUri,
            context.Credential,
            this.pluginVersion,
            cancellationToken).ConfigureAwait(false);
        return response.Relays.ToImmutableArray();
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

            if (this.State.Status is RelayRuntimeStatus.Loading or RelayRuntimeStatus.Working)
            {
                Volatile.Write(
                    ref this.state,
                    this.State with
                    {
                        Status = RelayRuntimeStatus.SuspendedDuty,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Message = null,
                    });
            }

            return;
        }

        while (this.frameworkActions.TryDequeue(out var action))
            action();

        if (this.State.Status == RelayRuntimeStatus.SuspendedDuty)
        {
            Volatile.Write(
                ref this.state,
                this.State with
                {
                    Status = RelayRuntimeStatus.Idle,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
        }
    }

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

    private sealed record RelayApiContext(Uri ServerBaseUri, string Credential);

    private sealed record RelayOperationCompletion(
        ImmutableArray<RelayResponse> Relays,
        Guid? ManagedRelayId = null,
        ImmutableArray<RelayMemberResponse> Members = default,
        ImmutableArray<RelayInvitationResponse> Invitations = default,
        RelayInvitationPreviewResponse? InvitationPreview = null,
        string? CreatedInvitationToken = null,
        Guid? AutoSelectedRelayId = null,
        bool MembershipListIsAuthoritative = false,
        string? Message = null);
}
