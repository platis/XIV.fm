using XIV.fm.Plugin.Core.Presence;

namespace XIV.fm.Plugin.Core.Tests.Presence;

public sealed class RelaySelectionTests
{
    [Fact]
    public void NormalizeHandlesASelectionSmallerThanTheMaximumCapacity()
    {
        var relayId = Guid.NewGuid();

        Assert.Empty(RelaySelection.Normalize(null));
        Assert.Empty(RelaySelection.Normalize([]));
        Assert.Equal(relayId, Assert.Single(RelaySelection.Normalize([relayId])));
    }

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

    [Fact]
    public void SelectAddsTheNewRelayAndKeepsItSelectedAtCapacity()
    {
        var relayIds = Enumerable.Range(0, RelaySelection.MaximumSelectedRelays)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var newRelayId = Guid.NewGuid();

        var selected = RelaySelection.Select(relayIds, newRelayId);

        Assert.Equal(RelaySelection.MaximumSelectedRelays, selected.Length);
        Assert.DoesNotContain(relayIds[0], selected);
        Assert.Equal(newRelayId, selected[^1]);
    }

    [Fact]
    public void SelectDoesNotDuplicateAnExistingRelay()
    {
        var relayIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var selected = RelaySelection.Select(relayIds, relayIds[0]);

        Assert.Equal(2, selected.Length);
        Assert.Equal(relayIds[0], selected[^1]);
    }
}
