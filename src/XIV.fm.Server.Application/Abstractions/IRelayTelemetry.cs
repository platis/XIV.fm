namespace XIV.fm.Server.Application.Abstractions;

public interface IRelayTelemetry
{
    void RecordCreated();

    void RecordDeleted();

    void RecordInvitationCreated();

    void RecordJoined();

    void RecordLeft();

    void RecordKicked();
}
