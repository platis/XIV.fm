using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using XIV.fm.Plugin.Core.Overlay;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using NativeVector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace XIV.fm.Plugin.Adapters;

/// <summary>
/// Reads the pose-aware world position that the game currently uses for a player's nameplate.
/// See docs/adr/0001-pose-aware-nameplate-anchor.md for the approved native-access boundary.
/// </summary>
internal static class DalamudNameplateAnchor
{
    public static unsafe bool TryGetWorldPosition(IPlayerCharacter player, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (!player.IsValid() || player.Address == nint.Zero)
            return false;

        var character = (NativeCharacter*)player.Address;

        NativeVector3 nativePosition = default;
        if (character->GameObject.GetNamePlateWorldPosition(&nativePosition) is null)
            return false;

        var candidate = new Vector3(nativePosition.X, nativePosition.Y, nativePosition.Z);
        if (!OverlayAnchor.IsValidNameplatePosition(player.Position, candidate))
            return false;

        worldPosition = candidate;
        return true;
    }
}
