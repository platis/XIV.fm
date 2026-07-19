using System.Numerics;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class OverlayAnchorTests
{
    [Fact]
    public void PoseAwareNameplatePositionReportsCurrentHeight()
    {
        var characterPosition = new Vector3(10f, 20f, 30f);
        var nameplatePosition = new Vector3(10.1f, 21.25f, 29.9f);

        Assert.True(OverlayAnchor.IsValidNameplatePosition(characterPosition, nameplatePosition));
        Assert.Equal(1.25f, OverlayAnchor.GetHeightYalms(characterPosition, nameplatePosition));
    }

    [Theory]
    [MemberData(nameof(InvalidPositions))]
    public void InvalidNameplatePositionsFailClosed(Vector3 characterPosition, Vector3 nameplatePosition)
    {
        Assert.False(OverlayAnchor.IsValidNameplatePosition(characterPosition, nameplatePosition));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayAnchor.GetHeightYalms(characterPosition, nameplatePosition));
    }

    public static TheoryData<Vector3, Vector3> InvalidPositions => new()
    {
        { Vector3.Zero, Vector3.Zero },
        { Vector3.Zero, new Vector3(0f, -1f, 0f) },
        { Vector3.Zero, new Vector3(0f, OverlayAnchor.MaximumNameplateOffsetYalms + 1f, 0f) },
        { new Vector3(float.NaN, 0f, 0f), Vector3.One },
        { Vector3.Zero, new Vector3(0f, float.PositiveInfinity, 0f) },
    };

    [Fact]
    public void PlaceholderContentIsClearlyNonProduction()
    {
        Assert.Contains("Placeholder", PlaceholderCard.Default.Title, StringComparison.Ordinal);
        Assert.Contains("development", PlaceholderCard.Default.Artist, StringComparison.OrdinalIgnoreCase);
    }
}
