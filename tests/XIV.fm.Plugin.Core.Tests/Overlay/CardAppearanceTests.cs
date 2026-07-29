using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class CardAppearanceTests
{
    [Fact]
    public void DefaultOpacityIsSixtyPercent()
    {
        Assert.Equal(60, CardAppearance.DefaultOpacityPercent);
        Assert.Equal(0.6f, CardAppearance.ToOpacity(CardAppearance.DefaultOpacityPercent));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(60, 60)]
    [InlineData(101, 100)]
    public void OpacityIsClamped(int configured, int expected)
    {
        Assert.Equal(expected, CardAppearance.NormalizeOpacityPercent(configured));
    }

    [Fact]
    public void DefaultCardSizeIsOneHundredPercent()
    {
        Assert.Equal(100, CardAppearance.DefaultSizePercent);
        Assert.Equal(1f, CardAppearance.ToScale(CardAppearance.DefaultSizePercent));
    }

    [Theory]
    [InlineData(49, 50)]
    [InlineData(100, 100)]
    [InlineData(151, 150)]
    public void CardSizeIsClamped(int configured, int expected)
    {
        Assert.Equal(expected, CardAppearance.NormalizeSizePercent(configured));
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(2f, 1f)]
    [InlineData(5f, 0.825f)]
    [InlineData(8f, 0.65f)]
    [InlineData(20f, 0.65f)]
    public void RemoteCardsShrinkSmoothlyAcrossTheVisibleRange(float distance, float expected)
    {
        var scale = CardAppearance.ScaleForRemoteDistance(1f, distance, 8);

        Assert.Equal(expected, scale, precision: 3);
    }

    [Fact]
    public void RemoteDistanceScalingPreservesConfiguredAndReadableBounds()
    {
        Assert.Equal(0.5f, CardAppearance.ScaleForRemoteDistance(0.5f, 8f, 8));
        Assert.Equal(0.975f, CardAppearance.ScaleForRemoteDistance(1.5f, 8f, 8), precision: 3);
        Assert.Equal(1f, CardAppearance.ScaleForRemoteDistance(1f, float.NaN, 8));
    }
}
