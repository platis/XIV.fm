namespace XIV.fm.Plugin.Core.Overlay;

public static class CardAppearance
{
    public const int DefaultOpacityPercent = 60;
    public const int MinimumOpacityPercent = 0;
    public const int MaximumOpacityPercent = 100;
    public const int DefaultSizePercent = 100;
    public const int MinimumSizePercent = 50;
    public const int MaximumSizePercent = 150;

    public static int NormalizeOpacityPercent(int opacityPercent) =>
        Math.Clamp(opacityPercent, MinimumOpacityPercent, MaximumOpacityPercent);

    public static float ToOpacity(int opacityPercent) =>
        NormalizeOpacityPercent(opacityPercent) / 100f;

    public static int NormalizeSizePercent(int sizePercent) =>
        Math.Clamp(sizePercent, MinimumSizePercent, MaximumSizePercent);

    public static float ToScale(int sizePercent) =>
        NormalizeSizePercent(sizePercent) / 100f;
}
