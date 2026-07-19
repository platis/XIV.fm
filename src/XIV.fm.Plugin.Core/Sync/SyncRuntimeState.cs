namespace XIV.fm.Plugin.Core.Sync;

public enum SyncRuntimeStatus
{
    Disabled,
    Waiting,
    Syncing,
    Healthy,
    Failed,
    SuspendedDuty,
}

public sealed record SyncRuntimeState(
    SyncRuntimeStatus Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? NextSyncAt = null,
    string? LastRequestId = null,
    string? Error = null)
{
    public static SyncRuntimeState Disabled { get; } =
        new(SyncRuntimeStatus.Disabled, DateTimeOffset.MinValue);
}
