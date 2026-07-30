using System.Globalization;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class TextEllipsisTests
{
    [Fact]
    public void LeavesTextUnchangedWhenItFits()
    {
        Assert.Equal("Artist", TextEllipsis.Fit("Artist", 6f, text => text.Length));
    }

    [Fact]
    public void ReplacesOverflowingTailWithThreeDots()
    {
        Assert.Equal("abcd...", TextEllipsis.Fit("abcdefghij", 7f, text => text.Length));
    }

    [Fact]
    public void DoesNotSplitUnicodeTextElements()
    {
        static float MeasureTextElements(string text) => StringInfo.ParseCombiningCharacters(text).Length;

        Assert.Equal("😀a...", TextEllipsis.Fit("😀abcdef", 5f, MeasureTextElements));
    }

    [Fact]
    public void ReturnsEmptyTextWhenTheSuffixCannotFit()
    {
        Assert.Equal(string.Empty, TextEllipsis.Fit("Artist", 2f, text => text.Length));
    }
}
