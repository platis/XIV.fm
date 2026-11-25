using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Presence;
using XIV.fm.Server.Domain.Installations;
using DomainVisibilityMode = XIV.fm.Server.Domain.Presence.VisibilityMode;

namespace XIV.fm.Server.Application.Accounts;

public sealed class AccountDeletionService
{
    private readonly ILinkedAccountResolver linkedAccountResolver;
    private readonly IAccountDeletionStore deletionStore;
    private readonly IPresenceStore presenceStore;
    private readonly IListeningStateStore listeningStateStore;
    private readonly IListeningPollingCoordinator pollingCoordinator;
    private readonly PublicPresenceSnapshotService publicSnapshotService;
    private readonly IRelayPresenceSnapshotCache relaySnapshotCache;
    private readonly TimeProvider timeProvider;

    public AccountDeletionService(
        ILinkedAccountResolver linkedAccountResolver,
        IAccountDeletionStore deletionStore,
        IPresenceStore presenceStore,
        IListeningStateStore listeningStateStore,
        IListeningPollingCoordinator pollingCoordinator,
        PublicPresenceSnapshotService publicSnapshotService,
        IRelayPresenceSnapshotCache relaySnapshotCache,
        TimeProvider timeProvider)
    {
        this.linkedAccountResolver = linkedAccountResolver;
        this.deletionStore = deletionStore;
        this.presenceStore = presenceStore;
        this.listeningStateStore = listeningStateStore;
        this.pollingCoordinator = pollingCoordinator;
        this.publicSnapshotService = publicSnapshotService;
        this.relaySnapshotCache = relaySnapshotCache;
        this.timeProvider = timeProvider;
    }

    /// <returns>Whether the installation was linked to an account that is now deleted.</returns>
    public async ValueTask<bool> DeleteAsync(InstallationId installationId, CancellationToken cancellationToken)
    {
        var account = await this.linkedAccountResolver
            .GetForInstallationAsync(installationId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
            return false;

        var deleted = await this.deletionStore
            .DeleteAsync(account.AccountId, this.timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        // The durable deletion is final, so ephemeral cleanup no longer follows the caller's cancellation.
        this.pollingCoordinator.Forget(account);
        foreach (var installation in deleted.Installations)
        {
            var removed = await this.presenceStore
                .RemoveInstallationAsync(installation, CancellationToken.None)
                .ConfigureAwait(false);
            if (removed?.Visibility.Mode == DomainVisibilityMode.Public)
            {
                await this.publicSnapshotService
                    .InvalidateAsync(removed.Location, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        foreach (var relayId in deleted.DeletedRelayIds.Concat(deleted.LeftRelayIds))
            await this.relaySnapshotCache.RemoveRelayAsync(relayId, CancellationToken.None).ConfigureAwait(false);
        await this.listeningStateStore
            .RemoveAsync(account.AccountId, CancellationToken.None)
            .ConfigureAwait(false);
        return true;
    }
}
