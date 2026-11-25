namespace XIV.fm.Contracts.V1;

/// <summary>
/// One installation linked to the authenticated account. <paramref name="LastUsedAt"/> is approximate.
/// </summary>
public sealed record InstallationResponse(
    Guid InstallationId,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastUsedAt,
    bool IsCurrent);

public sealed record InstallationListResponse(IReadOnlyList<InstallationResponse> Installations);
