namespace XIV.fm.Server.Application.Abstractions;

public interface IListeningPollingTelemetry
{
    void RecordCacheRead(bool found);

    void RecordPollSuccess();

    void RecordPollFailure();

    void RecordLeaseContention();
}

public sealed class NullListeningPollingTelemetry : IListeningPollingTelemetry
{
    public static NullListeningPollingTelemetry Instance { get; } = new();

    private NullListeningPollingTelemetry()
    {
    }

    public void RecordCacheRead(bool found)
    {
    }

    public void RecordPollSuccess()
    {
    }

    public void RecordPollFailure()
    {
    }

    public void RecordLeaseContention()
    {
    }
}
