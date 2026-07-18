using System.Numerics;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class OverlayAnchorTests
{
    [Fact]
    public void AboveCharacterOffsetsOnlyTheVerticalAxis()
    {
        var characterPosition = new Vector3(10, 20, 30);

        var anchor = OverlayAnchor.AboveCharacter(characterPosition, 2.5f);

        Assert.Equal(new Vector3(10, 22.5f, 30), anchor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AboveCharacterRejectsNonPositiveHeight(float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayAnchor.AboveCharacter(Vector3.Zero, height));
    }

    [Fact]
    public void PlaceholderContentIsClearlyNonProduction()
    {
        Assert.Contains("Placeholder", PlaceholderCard.Default.Title, StringComparison.Ordinal);
        Assert.Contains("development", PlaceholderCard.Default.Artist, StringComparison.OrdinalIgnoreCase);
    }
}
