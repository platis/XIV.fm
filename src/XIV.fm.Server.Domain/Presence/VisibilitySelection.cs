namespace XIV.fm.Server.Domain.Presence;

public enum VisibilityMode
{
    Private,
    Public,
    Custom,
}

public sealed record VisibilitySelection(
    VisibilityMode Mode,
    IReadOnlyList<Guid> RelayIds);
