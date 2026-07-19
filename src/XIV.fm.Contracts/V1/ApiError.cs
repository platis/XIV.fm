namespace XIV.fm.Contracts.V1;

/// <summary>
/// Stable RFC 9457-style error response with a machine-readable XIV.fm code.
/// </summary>
public sealed record ApiError(
    Uri Type,
    string Title,
    int Status,
    string Code,
    string RequestId,
    string? Detail = null,
    string? Instance = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
