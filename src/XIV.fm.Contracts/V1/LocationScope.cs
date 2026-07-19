namespace XIV.fm.Contracts.V1;

/// <summary>
/// Identifies the current game location without transmitting character coordinates.
/// </summary>
public sealed record LocationScope(
    uint CurrentWorldId,
    uint TerritoryId,
    uint MapId,
    uint InstanceId);
