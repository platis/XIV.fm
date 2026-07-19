using Dalamud.Configuration;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    public bool HasSeenAccountOnboarding { get; set; }

    public bool ShowPlaceholderCards { get; set; } = true;

    public bool DeveloperMockRemoteCards { get; set; }

    public string ServerBaseUrl { get; set; } = "https://xiv.fm";

    public string InstallationCredential { get; set; } = string.Empty;

    public string? LinkedLastFmAccountName { get; set; }

    public VisibilityMode Visibility { get; set; } = VisibilityMode.Private;

    public Guid? PendingLinkSessionId { get; set; }

    public string PendingLinkCredential { get; set; } = string.Empty;

    public DateTimeOffset? PendingLinkExpiresAt { get; set; }

    public bool DeveloperServerEnabled { get; set; }

    public string DeveloperServerBaseUrl { get; set; } = "http://127.0.0.1:5080";

    public string DeveloperInstallationCredential { get; set; } = string.Empty;

    public int RemoteCardDistanceYalms { get; set; } = OverlayVisibility.DefaultRemoteDistanceYalms;

    public int NormalizedRemoteCardDistanceYalms =>
        OverlayVisibility.NormalizeRemoteDistance(this.RemoteCardDistanceYalms);
}
