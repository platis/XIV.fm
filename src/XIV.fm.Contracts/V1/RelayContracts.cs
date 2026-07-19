namespace XIV.fm.Contracts.V1;

public sealed record CreateRelayRequest(string Name, Guid IdempotencyKey);

public sealed record RenameRelayRequest(string Name);

public sealed record RelayResponse(
    Guid RelayId,
    string Name,
    bool IsOwner,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RelayListResponse(IReadOnlyList<RelayResponse> Relays);

public sealed record RelayMemberResponse(
    Guid MembershipId,
    string LastFmAccountName,
    bool IsOwner,
    DateTimeOffset JoinedAt);

public sealed record RelayMemberListResponse(IReadOnlyList<RelayMemberResponse> Members);

public sealed record CreateRelayInvitationRequest(int? LifetimeHours = null);

public sealed record RelayInvitationResponse(
    Guid InvitationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt);

public sealed record CreatedRelayInvitationResponse(
    Guid InvitationId,
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record RelayInvitationListResponse(IReadOnlyList<RelayInvitationResponse> Invitations);

public sealed record RelayInvitationTokenRequest(string Token);

public sealed record RelayInvitationPreviewResponse(
    Guid RelayId,
    string RelayName,
    DateTimeOffset ExpiresAt);

public sealed record AcceptRelayInvitationResponse(RelayResponse Relay);
