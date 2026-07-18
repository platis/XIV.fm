using Dalamud.Game.ClientState.Objects.SubKinds;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Adapters;

public static class DalamudCharacterIdentity
{
    public static CharacterIdentity From(IPlayerCharacter player) =>
        new(player.Name.ToString(), player.HomeWorld.RowId);
}
