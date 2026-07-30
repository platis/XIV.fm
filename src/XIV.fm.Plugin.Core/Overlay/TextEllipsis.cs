using System.Globalization;

namespace XIV.fm.Plugin.Core.Overlay;

public static class TextEllipsis
{
    public const string Suffix = "...";

    public static string Fit(string text, float availableWidth, Func<string, float> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureWidth);

        if (!float.IsFinite(availableWidth) || availableWidth <= 0f)
            return string.Empty;
        if (measureWidth(text) <= availableWidth)
            return text;
        if (measureWidth(Suffix) > availableWidth)
            return string.Empty;

        var textElementStarts = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = textElementStarts.Length;
        while (low < high)
        {
            var candidateLength = (low + high + 1) / 2;
            var endIndex = candidateLength == textElementStarts.Length
                ? text.Length
                : textElementStarts[candidateLength];
            var candidate = string.Concat(text.AsSpan(0, endIndex), Suffix);
            if (measureWidth(candidate) <= availableWidth)
                low = candidateLength;
            else
                high = candidateLength - 1;
        }

        var prefixEndIndex = low == textElementStarts.Length
            ? text.Length
            : textElementStarts[low];
        return string.Concat(text.AsSpan(0, prefixEndIndex), Suffix);
    }
}
