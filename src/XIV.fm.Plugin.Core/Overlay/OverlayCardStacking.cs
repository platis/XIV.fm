namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Defines the back-to-front render layers for overlapping listening cards.
/// </summary>
public static class OverlayCardStacking
{
    public const int RemoteLayer = 0;
    public const int LocalLayer = 1;
    public const int InterfaceLayer = 2;
    public const int CardLayerCount = 2;

    public static int GetLayer(bool isLocal) => isLocal ? LocalLayer : RemoteLayer;
}
