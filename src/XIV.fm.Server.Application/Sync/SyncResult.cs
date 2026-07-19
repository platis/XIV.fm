using XIV.fm.Contracts.V1;

namespace XIV.fm.Server.Application.Sync;

public sealed record SyncResult
{
    private SyncResult(SyncResponse? response, SyncFailure? failure)
    {
        this.Response = response;
        this.Failure = failure;
    }

    public SyncResponse? Response { get; }

    public SyncFailure? Failure { get; }

    public bool IsSuccess => this.Response is not null;

    public static SyncResult Success(SyncResponse response) => new(response, null);

    public static SyncResult Failed(SyncFailure failure) => new(null, failure);
}
