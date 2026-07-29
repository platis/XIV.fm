namespace XIV.fm.Plugin.Core.Overlay;

public static class CardAppearance
{
    public const int DefaultOpacityPercent = 60;
    public const int MinimumOpacityPercent = 0;
    public const int MaximumOpacityPercent = 100;
    public const int DefaultSizePercent = 100;
    public const int MinimumSizePercent = 50;
    public const int MaximumSizePercent = 150;
    public const float FullSizeRemoteDistanceYalms = 2f;
    public const float MinimumRemoteDistanceScale = 0.65f;

    public static int NormalizeOpacityPercent(int opacityPercent) =>
        Math.Clamp(opacityPercent, MinimumOpacityPercent, MaximumOpacityPercent);

    public static float ToOpacity(int opacityPercent) =>
        NormalizeOpacityPercent(opacityPercent) / 100f;

    public static int NormalizeSizePercent(int sizePercent) =>
        Math.Clamp(sizePercent, MinimumSizePercent, MaximumSizePercent);

    public static float ToScale(int sizePercent) =>
        NormalizeSizePercent(sizePercent) / 100f;

    public static float ScaleForRemoteDistance(
        float configuredScale,
        float distanceYalms,
        int maximumDistanceYalms)
    {
        var normalizedConfiguredScale = Math.Clamp(
            configuredScale,
            MinimumSizePercent / 100f,
            MaximumSizePercent / 100f);
        var normalizedMaximumDistance = OverlayVisibility.NormalizeRemoteDistance(maximumDistanceYalms);
        if (!float.IsFinite(distanceYalms) ||
            distanceYalms <= FullSizeRemoteDistanceYalms ||
            normalizedMaximumDistance <= FullSizeRemoteDistanceYalms)
        {
            return normalizedConfiguredScale;
        }

        var progress = Math.Clamp(
            (distanceYalms - FullSizeRemoteDistanceYalms) /
            (normalizedMaximumDistance - FullSizeRemoteDistanceYalms),
            0f,
            1f);
        var smoothProgress = progress * progress * (3f - (2f * progress));
        var distanceScale = 1f + ((MinimumRemoteDistanceScale - 1f) * smoothProgress);
        return Math.Clamp(
            normalizedConfiguredScale * distanceScale,
            MinimumSizePercent / 100f,
            MaximumSizePercent / 100f);
    }
}
