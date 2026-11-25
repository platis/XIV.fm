using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Network;

public interface IInstallationApiClient
{
    Task RevokeCurrentInstallationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken);

    Task<InstallationListResponse> ListInstallationsAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken);

    Task RevokeInstallationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid installationId,
        CancellationToken cancellationToken);

    Task DeleteAccountAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken);
}
