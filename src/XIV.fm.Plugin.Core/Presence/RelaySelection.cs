using System.Collections.Immutable;

namespace XIV.fm.Plugin.Core.Presence;

public static class RelaySelection
{
    public const int MaximumSelectedRelays = 5;

    public static ImmutableArray<Guid> Normalize(IEnumerable<Guid>? relayIds)
    {
        if (relayIds is null)
            return [];

        var seen = new HashSet<Guid>();
        var builder = ImmutableArray.CreateBuilder<Guid>(MaximumSelectedRelays);
        foreach (var relayId in relayIds)
        {
            if (relayId == Guid.Empty || !seen.Add(relayId))
                continue;

            builder.Add(relayId);
            if (builder.Count == MaximumSelectedRelays)
                break;
        }

        return builder.MoveToImmutable();
    }

    public static bool CanSelect(IEnumerable<Guid>? relayIds, Guid relayId)
    {
        if (relayId == Guid.Empty)
            return false;

        var selected = Normalize(relayIds);
        return selected.Contains(relayId) || selected.Length < MaximumSelectedRelays;
    }
}
