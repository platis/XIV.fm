using Dalamud.Configuration;

namespace XIV.fm.Plugin;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ShowPlaceholderCard { get; set; } = true;
}
