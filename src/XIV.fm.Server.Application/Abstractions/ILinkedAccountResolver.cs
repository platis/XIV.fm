using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Application.Abstractions;

public sealed record LinkedLastFmAccount(AccountId AccountId, LastFmAccountIdentity Identity);

public interface ILinkedAccountResolver
{
    ValueTask<LinkedLastFmAccount?> GetForInstallationAsync(
        InstallationId installationId,
        CancellationToken cancellationToken);
}
