using System.Numerics;

namespace XIV.fm.Plugin.Core.Overlay;

public static class OverlayVisibility
{
    public const int DefaultRemoteDistanceYalms = 8;
    public const int MinimumRemoteDistanceYalms = 1;
    public const int MaximumRemoteDistanceYalms = 20;

    public static int NormalizeRemoteDistance(int distanceYalms) =>
        Math.Clamp(distanceYalms, MinimumRemoteDistanceYalms, MaximumRemoteDistanceYalms);

    public static bool IsRemoteWithinRange(
        Vector3 localPosition,
        Vector3 remotePosition,
        int maximumDistanceYalms)
    {
        var normalizedDistance = NormalizeRemoteDistance(maximumDistanceYalms);
        return Vector3.DistanceSquared(localPosition, remotePosition) <= normalizedDistance * normalizedDistance;
    }
}
