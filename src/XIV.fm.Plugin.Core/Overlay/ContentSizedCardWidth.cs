namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Sizes a card to its widest text line and optional leading content without exceeding its maximum width.
/// </summary>
public static class ContentSizedCardWidth
{
    public static float Calculate(
        float maximumWidth,
        float horizontalPadding,
        float leadingContentWidth,
        float titleWidth,
        float artistWidth)
    {
        if (!float.IsFinite(maximumWidth) || maximumWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        ValidateNonNegative(horizontalPadding, nameof(horizontalPadding));
        ValidateNonNegative(leadingContentWidth, nameof(leadingContentWidth));
        ValidateNonNegative(titleWidth, nameof(titleWidth));
        ValidateNonNegative(artistWidth, nameof(artistWidth));

        var contentWidth = (2f * horizontalPadding) + leadingContentWidth + MathF.Max(titleWidth, artistWidth);
        return MathF.Min(maximumWidth, contentWidth);
    }

    private static void ValidateNonNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
