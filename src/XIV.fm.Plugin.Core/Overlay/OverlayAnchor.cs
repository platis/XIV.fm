using System.Numerics;

namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Computes the world-space point used to place a card above a character.
/// Screen projection remains a Dalamud adapter responsibility.
/// </summary>
public static class OverlayAnchor
{
    public const float DefaultHeightYalms = 2.45f;

    public static Vector3 AboveCharacter(Vector3 characterPosition, float heightYalms = DefaultHeightYalms)
    {
        if (!float.IsFinite(heightYalms) || heightYalms <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightYalms), "Anchor height must be finite and positive.");

        return characterPosition + new Vector3(0, heightYalms, 0);
    }
}
