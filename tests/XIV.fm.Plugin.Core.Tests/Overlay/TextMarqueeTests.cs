using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class TextMarqueeTests
{
    [Theory]
    [InlineData(99f, 100f, false)]
    [InlineData(100f, 100f, false)]
    [InlineData(101f, 100f, true)]
    public void ScrollsOnlyWhenTextOverflows(float textWidth, float availableWidth, bool expected)
    {
        Assert.Equal(expected, TextMarquee.ShouldScroll(textWidth, availableWidth));
    }

    [Fact]
    public void HoldsBeforeMovingAtAConstantSpeed()
    {
        Assert.Equal(0f, TextMarquee.CalculateOffset(1d, 120f, 24f, 18f));
        Assert.Equal(9f, TextMarquee.CalculateOffset(1.75d, 120f, 24f, 18f), precision: 3);
        Assert.Equal(18f, TextMarquee.CalculateOffset(2.25d, 120f, 24f, 18f), precision: 3);
    }

    [Fact]
    public void DistanceScalingDoesNotChangeTheAnimationPhase()
    {
        const double elapsedSeconds = 2.25d;
        const float textWidth = 120f;

        var fullSizeOffset = TextMarquee.CalculateScaledOffset(elapsedSeconds, textWidth, 1f);
        var distantOffset = TextMarquee.CalculateScaledOffset(elapsedSeconds, textWidth, 0.65f);

        Assert.Equal(fullSizeOffset * 0.65f, distantOffset, precision: 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    public void InvalidDisplayScaleDoesNotMoveText(float displayScale)
    {
        Assert.Equal(0f, TextMarquee.CalculateScaledOffset(2d, 120f, displayScale));
    }

    [Fact]
    public void LoopsSeamlesslyBackToTheReadingPause()
    {
        const float textWidth = 120f;
        const float gap = 24f;
        const float speed = 18f;
        var cycleSeconds = TextMarquee.HoldSeconds + ((textWidth + gap) / speed);

        Assert.Equal(0f, TextMarquee.CalculateOffset(cycleSeconds, textWidth, gap, speed));
        Assert.Equal(0f, TextMarquee.CalculateOffset(cycleSeconds + 0.5d, textWidth, gap, speed));
    }
}
