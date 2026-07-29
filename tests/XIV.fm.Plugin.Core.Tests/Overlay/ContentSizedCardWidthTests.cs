using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class ContentSizedCardWidthTests
{
    [Theory]
    [InlineData(0f, 80f, 100f, 112f)]
    [InlineData(66.2f, 80f, 100f, 178.2f)]
    [InlineData(66.2f, 250f, 200f, 293f)]
    public void SizesToContentAndCapsAtMaximum(
        float leadingContentWidth,
        float titleWidth,
        float artistWidth,
        float expected)
    {
        var width = ContentSizedCardWidth.Calculate(
            maximumWidth: 293f,
            horizontalPadding: 6f,
            leadingContentWidth,
            titleWidth,
            artistWidth);

        Assert.Equal(expected, width, precision: 3);
    }
}
