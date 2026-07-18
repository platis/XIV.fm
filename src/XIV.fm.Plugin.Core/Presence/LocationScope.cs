namespace XIV.fm.Plugin.Core.Presence;

/// <summary>
/// Identifies the typed game location used to share a bounded presence snapshot.
/// </summary>
public readonly record struct LocationScope(
    uint CurrentWorldId,
    uint TerritoryId,
    uint MapId,
    uint InstanceId)
{
    public bool IsComplete => this.CurrentWorldId != 0 && this.TerritoryId != 0 && this.MapId != 0;

    public override string ToString() =>
        $"world={this.CurrentWorldId}; territory={this.TerritoryId}; map={this.MapId}; instance={this.InstanceId}";
}
