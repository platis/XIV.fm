using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Network;

public interface IAccountLinkApiClient
{
    Task<StartAccountLinkResponse> StartAccountLinkAsync(
        Uri serverBaseUri,
        string pluginVersion,
        CancellationToken cancellationToken);

    Task<AccountLinkStatusResponse> GetAccountLinkStatusAsync(
        Uri serverBaseUri,
        Guid linkSessionId,
        string linkCredential,
        string pluginVersion,
        CancellationToken cancellationToken);
}
