namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Defines the back-to-front render layers for overlapping listening cards.
/// </summary>
public static class OverlayCardStacking
{
    public const int RemoteLayer = 0;
    public const int LocalLayer = 1;
    public const int LayerCount = 2;

    public static int GetLayer(bool isLocal) => isLocal ? LocalLayer : RemoteLayer;
}
