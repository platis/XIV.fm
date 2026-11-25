using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Presence;
using XIV.fm.Server.Domain.Installations;
using DomainVisibilityMode = XIV.fm.Server.Domain.Presence.VisibilityMode;

namespace XIV.fm.Server.Application.Installations;

public sealed class InstallationApplicationService
{
    private readonly ILinkedAccountResolver linkedAccountResolver;
    private readonly IInstallationCredentialStore credentialStore;
    private readonly IPresenceStore presenceStore;
    private readonly PublicPresenceSnapshotService publicSnapshotService;
    private readonly RelayPresenceSnapshotService relaySnapshotService;

    public InstallationApplicationService(
        ILinkedAccountResolver linkedAccountResolver,
        IInstallationCredentialStore credentialStore,
        IPresenceStore presenceStore,
        PublicPresenceSnapshotService publicSnapshotService,
        RelayPresenceSnapshotService relaySnapshotService)
    {
        this.linkedAccountResolver = linkedAccountResolver;
        this.credentialStore = credentialStore;
        this.presenceStore = presenceStore;
        this.publicSnapshotService = publicSnapshotService;
        this.relaySnapshotService = relaySnapshotService;
    }

    /// <returns>The account's active installations, or null when the installation is not linked.</returns>
    public async ValueTask<IReadOnlyList<LinkedInstallation>?> ListAsync(
        InstallationId current,
        CancellationToken cancellationToken)
    {
        var account = await this.linkedAccountResolver
            .GetForInstallationAsync(current, cancellationToken)
            .ConfigureAwait(false);
        return account is null
            ? null
            : await this.linkedAccountResolver
                .ListInstallationsAsync(account.AccountId, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <returns>Whether the target is an active installation of the same account and is now revoked.</returns>
    public async ValueTask<bool> RevokeLinkedAsync(
        InstallationId current,
        InstallationId target,
        CancellationToken cancellationToken)
    {
        var installations = await this.ListAsync(current, cancellationToken).ConfigureAwait(false);
        if (installations is null || !installations.Any(installation => installation.InstallationId == target))
            return false;

        await this.RevokeAsync(target, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask RevokeAsync(InstallationId installationId, CancellationToken cancellationToken)
    {
        await this.credentialStore.RevokeAsync(installationId, cancellationToken).ConfigureAwait(false);
        var removedPresence = await this.presenceStore
            .RemoveInstallationAsync(installationId, cancellationToken)
            .ConfigureAwait(false);
        if (removedPresence?.Visibility.Mode == DomainVisibilityMode.Public)
        {
            await this.publicSnapshotService
                .InvalidateAsync(removedPresence.Location, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (removedPresence?.Visibility.Mode == DomainVisibilityMode.Custom)
        {
            await this.relaySnapshotService
                .InvalidateAsync(
                    removedPresence.Visibility.RelayIds,
                    removedPresence.Location,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
