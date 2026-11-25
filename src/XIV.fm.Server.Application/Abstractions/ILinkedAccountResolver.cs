using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Application.Abstractions;

public sealed record LinkedLastFmAccount(AccountId AccountId, LastFmAccountIdentity Identity);

public sealed record LinkedInstallation(
    InstallationId InstallationId,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastUsedAt);

public interface ILinkedAccountResolver
{
    ValueTask<LinkedLastFmAccount?> GetForInstallationAsync(
        InstallationId installationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists active installations, newest link first.
    /// </summary>
    ValueTask<IReadOnlyList<LinkedInstallation>> ListInstallationsAsync(
        AccountId accountId,
        CancellationToken cancellationToken);
}
