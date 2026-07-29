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
}
