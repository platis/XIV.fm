using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class HiddenPlayersTests
{
    private static readonly CharacterIdentity Alice = new("Alice Cat", 54);

    [Fact]
    public void HidingMatchesNameAndHomeWorld()
    {
        var players = new List<HiddenPlayer>();

        Assert.True(HiddenPlayers.TryAdd(players, Alice, "Faerie"));

        Assert.True(HiddenPlayers.IsHidden(players, new CharacterIdentity("alice cat", 54)));
        Assert.False(HiddenPlayers.IsHidden(players, new CharacterIdentity("Alice Cat", 55)));
        Assert.False(HiddenPlayers.IsHidden(players, new CharacterIdentity("Alice Bat", 54)));
        Assert.Equal("Alice Cat (Faerie)", Assert.Single(players).ToString());
    }

    [Fact]
    public void RepeatedUnknownAndExcessPlayersAreNotAdded()
    {
        var players = new List<HiddenPlayer>();
        Assert.True(HiddenPlayers.TryAdd(players, Alice, "Faerie"));
        Assert.False(HiddenPlayers.TryAdd(players, Alice, "Faerie"));
        Assert.False(HiddenPlayers.TryAdd(players, new CharacterIdentity("No World", 0), string.Empty));

        for (var index = players.Count; index < HiddenPlayers.Maximum; index++)
            Assert.True(HiddenPlayers.TryAdd(players, new CharacterIdentity($"Player {index}", 54), "Faerie"));
        Assert.False(HiddenPlayers.TryAdd(players, new CharacterIdentity("One Toomany", 54), "Faerie"));
    }

    [Fact]
    public void NormalizeDropsDamagedAndDuplicateEntries()
    {
        var normalized = HiddenPlayers.Normalize(
        [
            new HiddenPlayer("Alice Cat", 54, "Faerie"),
            new HiddenPlayer("ALICE CAT", 54, "Faerie"),
            new HiddenPlayer(" ", 54, "Faerie"),
            new HiddenPlayer("No World", 0, string.Empty),
            null,
        ]);

        Assert.Equal("Alice Cat", Assert.Single(normalized).Name);
        Assert.Empty(HiddenPlayers.Normalize(null));
    }
}
