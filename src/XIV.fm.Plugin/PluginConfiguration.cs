using Dalamud.Configuration;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool ShowPlaceholderCards { get; set; } = true;

    public bool DeveloperMockRemoteCards { get; set; }

    public bool DeveloperServerEnabled { get; set; }

    public string DeveloperServerBaseUrl { get; set; } = "http://127.0.0.1:5080";

    public string DeveloperInstallationCredential { get; set; } = string.Empty;

    public int RemoteCardDistanceYalms { get; set; } = OverlayVisibility.DefaultRemoteDistanceYalms;

    public int NormalizedRemoteCardDistanceYalms =>
        OverlayVisibility.NormalizeRemoteDistance(this.RemoteCardDistanceYalms);
}
