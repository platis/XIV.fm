using System.Globalization;

namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Fits user-visible text to a measured width without splitting a Unicode text element.
/// </summary>
public static class TextEllipsis
{
    public const string Suffix = "...";

    public static string Fit(string text, float maximumWidth, Func<string, float> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureWidth);
        if (!float.IsFinite(maximumWidth) || maximumWidth < 0f)
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));

        if (measureWidth(text) <= maximumWidth)
            return text;
        if (measureWidth(Suffix) >= maximumWidth)
            return Suffix;

        var textElementStarts = StringInfo.ParseCombiningCharacters(text);
        var minimumCount = 0;
        var maximumCount = textElementStarts.Length;
        while (minimumCount < maximumCount)
        {
            var candidateCount = (minimumCount + maximumCount + 1) / 2;
            var candidate = text[..GetEndIndex(text, textElementStarts, candidateCount)] + Suffix;
            if (measureWidth(candidate) <= maximumWidth)
                minimumCount = candidateCount;
            else
                maximumCount = candidateCount - 1;
        }

        return text[..GetEndIndex(text, textElementStarts, minimumCount)] + Suffix;
    }

    private static int GetEndIndex(string text, int[] textElementStarts, int count) =>
        count == textElementStarts.Length ? text.Length : textElementStarts[count];
}
