namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Remembers decoded identities of loaded players for a short time. Decoding a name from game memory on
/// every frame is the expensive part of matching cards.
/// </summary>
public sealed class LoadedPlayerIdentities
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly Dictionary<ulong, CharacterIdentity?> identities = [];
    private DateTimeOffset clearAt = DateTimeOffset.MinValue;

    public void BeginFrame(DateTimeOffset now)
    {
        if (now < this.clearAt)
            return;

        this.identities.Clear();
        this.clearAt = now.Add(Lifetime);
    }

    /// <summary>
    /// Returns null for a player whose identity is not available yet; that result is not remembered.
    /// </summary>
    public CharacterIdentity? Get<TPlayer>(ulong playerId, TPlayer player, Func<TPlayer, CharacterIdentity?> decode)
    {
        ArgumentNullException.ThrowIfNull(decode);
        if (this.identities.TryGetValue(playerId, out var known))
            return known;

        var identity = decode(player);
        if (identity is not null)
            this.identities[playerId] = identity;
        return identity;
    }
}
