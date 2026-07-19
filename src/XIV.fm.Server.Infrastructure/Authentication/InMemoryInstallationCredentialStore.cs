using System.Security.Cryptography;
using System.Text;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Infrastructure.Authentication;

public sealed class InMemoryInstallationCredentialStore : IInstallationCredentialStore
{
    private const int MinimumCredentialLength = 32;
    private const int MaximumCredentialLength = 512;

    private readonly Lock gate = new();
    private readonly Dictionary<string, InstallationId> installationsByHash = new(StringComparer.Ordinal);
    private readonly Dictionary<InstallationId, string> hashesByInstallation = [];

    public ValueTask<InstallationId?> AuthenticateAsync(string credential, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCredentialShapeValid(credential))
            return ValueTask.FromResult<InstallationId?>(null);

        var hash = Hash(credential);
        lock (this.gate)
        {
            return ValueTask.FromResult(
                this.installationsByHash.TryGetValue(hash, out var installationId)
                    ? (InstallationId?)installationId
                    : null);
        }
    }

    public async ValueTask<IssuedInstallationCredential> ProvisionAsync(CancellationToken cancellationToken)
    {
        var installationId = new InstallationId(Guid.NewGuid());
        var credential = InstallationCredentialGenerator.Generate();
        await this.RegisterAsync(installationId, credential, cancellationToken).ConfigureAwait(false);
        return new IssuedInstallationCredential(installationId, credential);
    }

    public async ValueTask<string> RotateAndIssueAsync(
        InstallationId installationId,
        CancellationToken cancellationToken)
    {
        var credential = InstallationCredentialGenerator.Generate();
        await this.RotateAsync(installationId, credential, cancellationToken).ConfigureAwait(false);
        return credential;
    }

    public ValueTask RegisterAsync(
        InstallationId installationId,
        string credential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCredential(credential);
        var hash = Hash(credential);
        lock (this.gate)
        {
            if (this.hashesByInstallation.ContainsKey(installationId))
                throw new InvalidOperationException("The installation already has a credential.");
            if (this.installationsByHash.ContainsKey(hash))
                throw new InvalidOperationException("The credential is already registered.");

            this.hashesByInstallation.Add(installationId, hash);
            this.installationsByHash.Add(hash, installationId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RotateAsync(
        InstallationId installationId,
        string newCredential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCredential(newCredential);
        var newHash = Hash(newCredential);
        lock (this.gate)
        {
            if (!this.hashesByInstallation.TryGetValue(installationId, out var oldHash))
                throw new InvalidOperationException("The installation does not have a credential.");
            if (this.installationsByHash.TryGetValue(newHash, out var owner) && owner != installationId)
                throw new InvalidOperationException("The credential is already registered.");

            this.installationsByHash.Remove(oldHash);
            this.hashesByInstallation[installationId] = newHash;
            this.installationsByHash[newHash] = installationId;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RevokeAsync(InstallationId installationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            if (this.hashesByInstallation.Remove(installationId, out var hash))
                this.installationsByHash.Remove(hash);
        }

        return ValueTask.CompletedTask;
    }

    private static bool IsCredentialShapeValid(string? credential) =>
        credential is not null &&
        credential.Length is >= MinimumCredentialLength and <= MaximumCredentialLength &&
        !credential.Any(char.IsWhiteSpace);

    private static void ValidateCredential(string credential)
    {
        if (!IsCredentialShapeValid(credential))
        {
            throw new ArgumentException(
                $"Credentials must contain {MinimumCredentialLength} to {MaximumCredentialLength} non-whitespace characters.",
                nameof(credential));
        }
    }

    private static string Hash(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
}
