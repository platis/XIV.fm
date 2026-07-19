using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Authentication;

namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class PostgresInstallationCredentialStore : IInstallationCredentialStore
{
    private const int MinimumCredentialLength = 32;
    private const int MaximumCredentialLength = 512;

    private readonly IDbContextFactory<XivFmDbContext> contextFactory;
    private readonly TimeProvider timeProvider;

    public PostgresInstallationCredentialStore(
        IDbContextFactory<XivFmDbContext> contextFactory,
        TimeProvider timeProvider)
    {
        this.contextFactory = contextFactory;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<InstallationId?> AuthenticateAsync(
        string credential,
        CancellationToken cancellationToken)
    {
        if (!IsCredentialShapeValid(credential))
            return null;

        var hash = Hash(credential);
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var installationId = await context.InstallationCredentials
            .AsNoTracking()
            .Where(entity => entity.CredentialHash == hash && entity.RevokedAt == null)
            .Select(entity => (Guid?)entity.InstallationId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return installationId is null ? null : new InstallationId(installationId.Value);
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

    public async ValueTask RegisterAsync(
        InstallationId installationId,
        string credential,
        CancellationToken cancellationToken)
    {
        ValidateCredential(credential);
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.InstallationCredentials.Add(new InstallationCredentialEntity
        {
            InstallationId = installationId.Value,
            CredentialHash = Hash(credential),
            CreatedAt = this.timeProvider.GetUtcNow(),
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("The installation or credential is already registered.", exception);
        }
    }

    public async ValueTask RotateAsync(
        InstallationId installationId,
        string newCredential,
        CancellationToken cancellationToken)
    {
        ValidateCredential(newCredential);
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await context.InstallationCredentials
            .SingleOrDefaultAsync(
                candidate => candidate.InstallationId == installationId.Value,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The installation does not have a credential.");
        if (entity.RevokedAt is not null)
            throw new InvalidOperationException("The installation credential is revoked.");

        entity.CredentialHash = Hash(newCredential);
        entity.RotatedAt = this.timeProvider.GetUtcNow();
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("The credential is already registered.", exception);
        }
    }

    public async ValueTask RevokeAsync(
        InstallationId installationId,
        CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await context.InstallationCredentials
            .SingleOrDefaultAsync(
                candidate => candidate.InstallationId == installationId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null || entity.RevokedAt is not null)
            return;

        entity.RevokedAt = this.timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
