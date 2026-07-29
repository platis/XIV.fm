using XIV.fm.Plugin.Core.Presence;

namespace XIV.fm.Plugin.Core.Tests.Presence;

public sealed class RelaySelectionTests
{
    [Fact]
    public void NormalizeDropsEmptyAndDuplicateIdsAndBoundsTheSelection()
    {
        var relayIds = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();

        var normalized = RelaySelection.Normalize(
            [Guid.Empty, relayIds[0], relayIds[0], .. relayIds.Skip(1)]);

        Assert.Equal(RelaySelection.MaximumSelectedRelays, normalized.Length);
        Assert.Equal(relayIds.Take(RelaySelection.MaximumSelectedRelays), normalized);
    }

    [Fact]
    public void CanSelectAllowsAnExistingRelayWhenTheSelectionIsFull()
    {
        var relayIds = Enumerable.Range(0, RelaySelection.MaximumSelectedRelays)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        Assert.True(RelaySelection.CanSelect(relayIds, relayIds[2]));
        Assert.False(RelaySelection.CanSelect(relayIds, Guid.NewGuid()));
        Assert.False(RelaySelection.CanSelect(relayIds, Guid.Empty));
    }
}
