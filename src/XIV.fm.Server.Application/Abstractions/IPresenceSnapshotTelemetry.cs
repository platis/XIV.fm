namespace XIV.fm.Server.Application.Abstractions;

public interface IPresenceSnapshotTelemetry
{
    void RecordSnapshotCacheRead(bool found);

    void RecordSnapshotBuild(int entryCount);
}

public sealed class NullPresenceSnapshotTelemetry : IPresenceSnapshotTelemetry
{
    public static NullPresenceSnapshotTelemetry Instance { get; } = new();

    private NullPresenceSnapshotTelemetry()
    {
    }

    public void RecordSnapshotCacheRead(bool found)
    {
    }

    public void RecordSnapshotBuild(int entryCount)
    {
    }
}
