namespace XIV.fm.Server.Domain.Presence;

public readonly record struct LocationScope
{
    public LocationScope(uint currentWorldId, uint territoryId, uint mapId, uint instanceId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(currentWorldId);
        ArgumentOutOfRangeException.ThrowIfZero(territoryId);
        ArgumentOutOfRangeException.ThrowIfZero(mapId);

        this.CurrentWorldId = currentWorldId;
        this.TerritoryId = territoryId;
        this.MapId = mapId;
        this.InstanceId = instanceId;
    }

    public uint CurrentWorldId { get; }

    public uint TerritoryId { get; }

    public uint MapId { get; }

    public uint InstanceId { get; }
}
