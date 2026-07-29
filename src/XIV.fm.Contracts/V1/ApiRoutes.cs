namespace XIV.fm.Contracts.V1;

public static class ApiRoutes
{
    public const string Sync = "/v1/sync";

    public const string StartAccountLink = "/v1/account-links";

    public const string AccountLinkStatus = "/v1/account-links/{linkSessionId:guid}/status";

    public const string CompleteAccountLink = "/v1/account-links/{linkSessionId:guid}/callback";

    public const string RotateCurrentInstallation = "/v1/installations/current/credential";

    public const string RevokeCurrentInstallation = "/v1/installations/current";

    public const string Relays = "/v1/relays";

    public const string Relay = "/v1/relays/{relayId:guid}";

    public const string RelayMembers = "/v1/relays/{relayId:guid}/members";

    public const string RelayMember = "/v1/relays/{relayId:guid}/members/{membershipId:guid}";

    public const string RelayMembership = "/v1/relays/{relayId:guid}/membership";

    public const string RelayInvitations = "/v1/relays/{relayId:guid}/invitations";

    public const string RelayInvitation = "/v1/relays/{relayId:guid}/invitations/{invitationId:guid}";

    public const string RelayInvitationPreview = "/v1/relay-invitations/preview";

    public const string RelayInvitationAccept = "/v1/relay-invitations/accept";

    public static string GetAccountLinkStatus(Guid linkSessionId) =>
        $"/v1/account-links/{linkSessionId:D}/status";

    public static string GetAccountLinkCallback(Guid linkSessionId) =>
        $"/v1/account-links/{linkSessionId:D}/callback";

    public static string GetRelay(Guid relayId) =>
        $"/v1/relays/{relayId:D}";

    public static string GetRelayMembers(Guid relayId) =>
        $"/v1/relays/{relayId:D}/members";

    public static string GetRelayMember(Guid relayId, Guid membershipId) =>
        $"/v1/relays/{relayId:D}/members/{membershipId:D}";

    public static string GetRelayMembership(Guid relayId) =>
        $"/v1/relays/{relayId:D}/membership";

    public static string GetRelayInvitations(Guid relayId) =>
        $"/v1/relays/{relayId:D}/invitations";

    public static string GetRelayInvitation(Guid relayId, Guid invitationId) =>
        $"/v1/relays/{relayId:D}/invitations/{invitationId:D}";
}
