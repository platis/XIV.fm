namespace XIV.fm.Plugin.Network;

public interface IInstallationApiClient
{
    Task RevokeCurrentInstallationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken);

    Task DeleteAccountAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken);
}
