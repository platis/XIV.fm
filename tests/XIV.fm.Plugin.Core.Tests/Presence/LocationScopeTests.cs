using XIV.fm.Plugin.Core.Presence;

namespace XIV.fm.Plugin.Core.Tests.Presence;

public sealed class LocationScopeTests
{
    [Fact]
    public void CompleteScopeIncludesWorldTerritoryMapAndInstance()
    {
        var scope = new LocationScope(54, 129, 130, 2);

        Assert.True(scope.IsComplete);
        Assert.Equal("world=54; territory=129; map=130; instance=2", scope.ToString());
    }

    [Theory]
    [InlineData(0, 129, 130)]
    [InlineData(54, 0, 130)]
    [InlineData(54, 129, 0)]
    public void MissingStableIdentifierMakesScopeIncomplete(uint world, uint territory, uint map)
    {
        var scope = new LocationScope(world, territory, map, 0);

        Assert.False(scope.IsComplete);
    }

    [Fact]
    public void ZeroInstanceIsValidForANonInstancedMap()
    {
        Assert.True(new LocationScope(54, 129, 130, 0).IsComplete);
    }
}
