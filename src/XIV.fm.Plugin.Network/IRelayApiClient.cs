using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Network;

public interface IRelayApiClient
{
    Task<RelayListResponse> ListRelaysAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken);

    Task<RelayResponse> CreateRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CreateRelayRequest request,
        CancellationToken cancellationToken);

    Task<RelayResponse> RenameRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        RenameRelayRequest request,
        CancellationToken cancellationToken);

    Task DeleteRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken);

    Task<RelayMemberListResponse> ListRelayMembersAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken);

    Task KickRelayMemberAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        Guid membershipId,
        CancellationToken cancellationToken);

    Task LeaveRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken);

    Task<CreatedRelayInvitationResponse> CreateRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CreateRelayInvitationRequest request,
        CancellationToken cancellationToken);

    Task<RelayInvitationListResponse> ListRelayInvitationsAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken);

    Task RevokeRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        Guid invitationId,
        CancellationToken cancellationToken);

    Task<RelayInvitationPreviewResponse> PreviewRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        RelayInvitationTokenRequest request,
        CancellationToken cancellationToken);

    Task<AcceptRelayInvitationResponse> AcceptRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        RelayInvitationTokenRequest request,
        CancellationToken cancellationToken);
}
