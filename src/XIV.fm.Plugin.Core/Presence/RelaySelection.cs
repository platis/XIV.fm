using System.Collections.Immutable;
using XIV.fm.Contracts.V1;

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

        return builder.ToImmutable();
    }

    public static bool CanSelect(IEnumerable<Guid>? relayIds, Guid relayId)
    {
        if (relayId == Guid.Empty)
            return false;

        var selected = Normalize(relayIds);
        return selected.Contains(relayId) || selected.Length < MaximumSelectedRelays;
    }

    /// <summary>
    /// Custom visibility needs an audience, so its last Relay stays until another visibility is chosen.
    /// </summary>
    public static bool CanDeselect(IEnumerable<Guid>? relayIds, Guid relayId, VisibilityMode visibility)
    {
        var selected = Normalize(relayIds);
        return selected.Contains(relayId) && (visibility != VisibilityMode.Custom || selected.Length > 1);
    }

    public static ImmutableArray<Guid> Select(IEnumerable<Guid>? relayIds, Guid relayId)
    {
        if (relayId == Guid.Empty)
            return Normalize(relayIds);

        var selected = Normalize(relayIds)
            .Where(existingRelayId => existingRelayId != relayId)
            .ToList();
        if (selected.Count == MaximumSelectedRelays)
            selected.RemoveAt(0);

        selected.Add(relayId);
        return Normalize(selected);
    }
}
