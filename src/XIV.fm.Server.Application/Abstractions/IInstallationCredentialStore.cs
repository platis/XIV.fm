using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Application.Abstractions;

public sealed record IssuedInstallationCredential(InstallationId InstallationId, string Credential);

public interface IInstallationCredentialStore
{
    ValueTask<InstallationId?> AuthenticateAsync(string credential, CancellationToken cancellationToken);

    ValueTask<IssuedInstallationCredential> ProvisionAsync(CancellationToken cancellationToken);

    ValueTask<string> RotateAndIssueAsync(InstallationId installationId, CancellationToken cancellationToken);

    ValueTask RegisterAsync(InstallationId installationId, string credential, CancellationToken cancellationToken);

    ValueTask RotateAsync(InstallationId installationId, string newCredential, CancellationToken cancellationToken);

    ValueTask RevokeAsync(InstallationId installationId, CancellationToken cancellationToken);
}
