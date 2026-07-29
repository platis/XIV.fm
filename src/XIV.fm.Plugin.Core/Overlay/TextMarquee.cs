namespace XIV.fm.Plugin.Core.Overlay;

public static class TextMarquee
{
    public const float HoldSeconds = 1.25f;
    public const float SpeedPixelsPerSecond = 18f;
    public const float GapPixels = 24f;

    public static bool ShouldScroll(float textWidth, float availableWidth) =>
        float.IsFinite(textWidth) &&
        float.IsFinite(availableWidth) &&
        availableWidth > 0f &&
        textWidth > availableWidth;

    public static float CalculateOffset(
        double elapsedSeconds,
        float textWidth,
        float gap,
        float speedPixelsPerSecond)
    {
        if (!double.IsFinite(elapsedSeconds) ||
            elapsedSeconds <= HoldSeconds ||
            !float.IsFinite(textWidth) ||
            textWidth <= 0f ||
            !float.IsFinite(gap) ||
            gap < 0f ||
            !float.IsFinite(speedPixelsPerSecond) ||
            speedPixelsPerSecond <= 0f)
        {
            return 0f;
        }

        var travelDistance = textWidth + gap;
        var travelSeconds = travelDistance / speedPixelsPerSecond;
        var cycleSeconds = HoldSeconds + travelSeconds;
        var cyclePosition = elapsedSeconds % cycleSeconds;
        if (cyclePosition <= HoldSeconds)
            return 0f;

        return Math.Min(
            (float)(cyclePosition - HoldSeconds) * speedPixelsPerSecond,
            travelDistance);
    }
}
