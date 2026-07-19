using System.Security.Cryptography;
using System.Text;
using XIV.fm.Contracts.V1;

namespace XIV.fm.Server.Application.Presence;

public static class PrivatePresenceSnapshot
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(20);

    public static SnapshotResult Create(
        LocationScope location,
        DateTimeOffset now,
        string? knownVersion)
    {
        var material = Encoding.UTF8.GetBytes(
            $"v1-private-empty:{location.CurrentWorldId}:{location.TerritoryId}:{location.MapId}:{location.InstanceId}");
        var version = Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant();
        var snapshot = string.Equals(knownVersion, version, StringComparison.Ordinal)
            ? null
            : new PresenceSnapshot(location, now, now.Add(Lifetime), []);
        return new SnapshotResult(version, snapshot);
    }
}
