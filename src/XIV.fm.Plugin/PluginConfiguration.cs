using Dalamud.Configuration;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public const string DefaultPublicServerBaseUrl = "https://api.xivfm.form-a.page";

    public const string LegacyPublicNginxBaseUrl = "https://xivfm.168.138.129.70.sslip.io";

    public const string LegacyPublicServerBaseUrl = "https://xiv.fm";

    public const string LegacyPublicFunnelBaseUrl = "https://oracle-dev.tail9c4140.ts.net";

    public const string DefaultDeveloperServerBaseUrl = "http://127.0.0.1:5080";

    public const int CurrentVersion = 11;

    public int Version { get; set; } = CurrentVersion;

    public bool HasSeenAccountOnboarding { get; set; }

    // Serialized name retained for configuration compatibility; this controls real listening cards.
    public bool ShowPlaceholderCards { get; set; } = true;

    public bool ShowOwnListeningCard { get; set; } = true;

    public bool DeveloperMockRemoteCards { get; set; }

    public string ServerBaseUrl { get; set; } = DefaultPublicServerBaseUrl;

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

    // Serialized name retained for configuration compatibility; this controls the local player's card.
    public int CardOpacityPercent { get; set; } = CardAppearance.DefaultOpacityPercent;

    public int OtherCardOpacityPercent { get; set; } = CardAppearance.DefaultOpacityPercent;

    public List<HiddenPlayer> HiddenPlayers { get; set; } = [];

    public bool OutlineCardText { get; set; }

    public int OwnCardSizePercent { get; set; } = CardAppearance.DefaultSizePercent;

    public int OtherCardSizePercent { get; set; } = CardAppearance.DefaultSizePercent;

    public int NormalizedRemoteCardDistanceYalms =>
        OverlayVisibility.NormalizeRemoteDistance(this.RemoteCardDistanceYalms);

    public int NormalizedOwnCardOpacityPercent =>
        CardAppearance.NormalizeOpacityPercent(this.CardOpacityPercent);

    public int NormalizedOtherCardOpacityPercent =>
        CardAppearance.NormalizeOpacityPercent(this.OtherCardOpacityPercent);

    public int NormalizedOwnCardSizePercent =>
        CardAppearance.NormalizeSizePercent(this.OwnCardSizePercent);

    public int NormalizedOtherCardSizePercent =>
        CardAppearance.NormalizeSizePercent(this.OtherCardSizePercent);

    /// <returns>Whether the configuration changed.</returns>
    public bool Migrate()
    {
        if (this.Version == CurrentVersion)
            return false;

        // Version 11 split one opacity into two; keep the look both cards already had.
        if (this.Version < 11)
            this.OtherCardOpacityPercent = this.CardOpacityPercent;
        this.Version = CurrentVersion;
        return true;
    }

    public static bool IsHostedPublicServer(string? baseUrl)
    {
        var normalized = baseUrl?.Trim().TrimEnd('/');
        return string.Equals(normalized, DefaultPublicServerBaseUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, LegacyPublicNginxBaseUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, LegacyPublicFunnelBaseUrl, StringComparison.OrdinalIgnoreCase);
    }
}
