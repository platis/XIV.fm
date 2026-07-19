using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Network;

public interface IServerSyncApiClient
{
    Task<ServerSyncApiResult> SyncAsync(
        Uri serverBaseUri,
        string installationCredential,
        SyncRequest request,
        CancellationToken cancellationToken);
}

public sealed record ServerSyncApiResult(SyncResponse Response, string? RequestId);
