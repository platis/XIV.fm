namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Finds remote cards by character in constant time. Building it once per snapshot replaces comparing every
/// card with every loaded player on every frame.
/// </summary>
public sealed class RemoteCardLookup
{
    private readonly Dictionary<CharacterIdentity, int> indexes;

    public RemoteCardLookup(OverlaySnapshot snapshot)
    {
        this.Snapshot = snapshot;
        this.indexes = new Dictionary<CharacterIdentity, int>(snapshot.Cards.Length, Comparer.Instance);
        for (var index = 0; index < snapshot.Cards.Length; index++)
        {
            var card = snapshot.Cards[index];
            if (!card.IsLocal && card.Character.HomeWorldId != 0)
                this.indexes.TryAdd(card.Character, index);
        }
    }

    public static RemoteCardLookup Empty { get; } = new(OverlaySnapshot.Empty);

    public OverlaySnapshot Snapshot { get; }

    public bool IsEmpty => this.indexes.Count == 0;

    /// <returns>The card's position in the snapshot, which is also its drawing order.</returns>
    public bool TryFind(CharacterIdentity character, out int index)
    {
        index = -1;
        return character.HomeWorldId != 0 && this.indexes.TryGetValue(character, out index);
    }

    private sealed class Comparer : IEqualityComparer<CharacterIdentity>
    {
        public static Comparer Instance { get; } = new();

        public bool Equals(CharacterIdentity first, CharacterIdentity second) =>
            first.HomeWorldId == second.HomeWorldId &&
            string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CharacterIdentity character) => HashCode.Combine(
            character.HomeWorldId,
            StringComparer.OrdinalIgnoreCase.GetHashCode(character.Name));
    }
}
