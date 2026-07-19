namespace XIV.fm.Server.Application.Sync;

public enum SyncFailureKind
{
    Validation,
    Conflict,
}

public sealed record SyncFailure(
    SyncFailureKind Kind,
    string Code,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
