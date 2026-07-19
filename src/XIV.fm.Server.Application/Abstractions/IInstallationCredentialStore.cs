using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Application.Abstractions;

public interface IInstallationCredentialStore
{
    ValueTask<InstallationId?> AuthenticateAsync(string credential, CancellationToken cancellationToken);

    ValueTask RegisterAsync(InstallationId installationId, string credential, CancellationToken cancellationToken);

    ValueTask RotateAsync(InstallationId installationId, string newCredential, CancellationToken cancellationToken);

    ValueTask RevokeAsync(InstallationId installationId, CancellationToken cancellationToken);
}
