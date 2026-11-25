namespace XIV.fm.Server.Application.Presence;

/// <summary>
/// Serializes work per key with a fixed number of locks. Unrelated keys may occasionally share a lock, but
/// memory use does not grow with the number of locations and Relays ever seen.
/// </summary>
internal sealed class StripedLock
{
    private const int StripeCount = 64;

    private readonly SemaphoreSlim[] stripes =
        Enumerable.Range(0, StripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public SemaphoreSlim Get<TKey>(TKey key)
        where TKey : notnull =>
        this.stripes[(key.GetHashCode() & int.MaxValue) % StripeCount];
}
