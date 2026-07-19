using System.Numerics;

namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Validates the pose-aware world point supplied by the game for a character nameplate.
/// Screen projection and native game access remain Dalamud adapter responsibilities.
/// </summary>
public static class OverlayAnchor
{
    public const float MaximumNameplateOffsetYalms = 50f;

    public static bool IsValidNameplatePosition(Vector3 characterPosition, Vector3 nameplatePosition)
    {
        if (!IsFinite(characterPosition) || !IsFinite(nameplatePosition))
            return false;

        var offset = nameplatePosition - characterPosition;
        return offset.Y > 0f &&
            offset.LengthSquared() <= MaximumNameplateOffsetYalms * MaximumNameplateOffsetYalms;
    }

    public static float GetHeightYalms(Vector3 characterPosition, Vector3 nameplatePosition)
    {
        if (!IsValidNameplatePosition(characterPosition, nameplatePosition))
            throw new ArgumentOutOfRangeException(nameof(nameplatePosition), "Nameplate position is not valid for the character.");

        return nameplatePosition.Y - characterPosition.Y;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
