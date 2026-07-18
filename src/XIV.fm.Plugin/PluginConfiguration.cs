using Dalamud.Configuration;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ShowPlaceholderCards { get; set; } = true;

    public bool DeveloperMockRemoteCards { get; set; }

    public int RemoteCardDistanceYalms { get; set; } = OverlayVisibility.DefaultRemoteDistanceYalms;

    public int NormalizedRemoteCardDistanceYalms =>
        OverlayVisibility.NormalizeRemoteDistance(this.RemoteCardDistanceYalms);
}
