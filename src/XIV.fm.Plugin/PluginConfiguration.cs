using Dalamud.Configuration;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public const string DefaultDeveloperServerBaseUrl = "https://xivfm.168.138.129.70.sslip.io";

    public const string LegacyDeveloperServerBaseUrl = "http://127.0.0.1:5080";

    public int Version { get; set; } = 9;

    public bool HasSeenAccountOnboarding { get; set; }

    // Serialized name retained for configuration compatibility; this controls real listening cards.
    public bool ShowPlaceholderCards { get; set; } = true;

    public bool ShowOwnListeningCard { get; set; } = true;

    public bool DeveloperMockRemoteCards { get; set; }

    public string ServerBaseUrl { get; set; } = "https://xiv.fm";

    public string InstallationCredential { get; set; } = string.Empty;

    public string? LinkedLastFmAccountName { get; set; }

    public VisibilityMode Visibility { get; set; } = VisibilityMode.Private;

    public List<Guid> SelectedRelayIds { get; set; } = [];

    public Guid? PendingLinkSessionId { get; set; }

    public string PendingLinkCredential { get; set; } = string.Empty;

    public DateTimeOffset? PendingLinkExpiresAt { get; set; }

    public string PendingLinkAuthorizationUrl { get; set; } = string.Empty;

    public bool DeveloperServerEnabled { get; set; }

    public string DeveloperServerBaseUrl { get; set; } = DefaultDeveloperServerBaseUrl;

    public string DeveloperInstallationCredential { get; set; } = string.Empty;

    public int RemoteCardDistanceYalms { get; set; } = OverlayVisibility.DefaultRemoteDistanceYalms;

    public int CardOpacityPercent { get; set; } = CardAppearance.DefaultOpacityPercent;

    public int OwnCardSizePercent { get; set; } = CardAppearance.DefaultSizePercent;

    public int OtherCardSizePercent { get; set; } = CardAppearance.DefaultSizePercent;

    public int NormalizedRemoteCardDistanceYalms =>
        OverlayVisibility.NormalizeRemoteDistance(this.RemoteCardDistanceYalms);

    public int NormalizedCardOpacityPercent =>
        CardAppearance.NormalizeOpacityPercent(this.CardOpacityPercent);

    public int NormalizedOwnCardSizePercent =>
        CardAppearance.NormalizeSizePercent(this.OwnCardSizePercent);

    public int NormalizedOtherCardSizePercent =>
        CardAppearance.NormalizeSizePercent(this.OtherCardSizePercent);
}
