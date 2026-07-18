using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Core.Presence;

namespace XIV.fm.Plugin.Adapters;

public static class DalamudLocationScope
{
    public static LocationScope? Capture(IClientState clientState, IPlayerCharacter localPlayer)
    {
        var scope = new LocationScope(
            localPlayer.CurrentWorld.RowId,
            clientState.TerritoryType,
            clientState.MapId,
            clientState.Instance);

        return scope.IsComplete ? scope : null;
    }
}
