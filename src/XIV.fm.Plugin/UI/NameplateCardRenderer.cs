using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Adapters;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Renders immutable local and remote card state through one player/nameplate anchoring path.
/// </summary>
public sealed class NameplateCardRenderer
{
    private static readonly Vector2 DesignCardSize = new(323f, 127f);
    private static readonly Vector2 DesignArtworkOffset = new(18f, 19f);
    private static readonly Vector2 DesignArtworkSize = new(87f, 87f);
    private static readonly Vector2 DesignTextOffset = new(121f, 56f);

    // The supplied SVG is the design source, while the in-game presentation is intentionally
    // reduced uniformly so its proportions remain unchanged without dominating nameplates.
    private const float CardDisplayScale = 0.7f;

    private const ImGuiWindowFlags CardWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoMouseInputs;

    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly OverlayStateStore stateStore;
    private readonly AlbumArtworkCache artworkCache;
    private readonly ISharedImmediateTexture developmentArtwork;
    private readonly Func<bool> isEnabled;
    private readonly Func<int> remoteDistanceYalms;
    private OverlayRenderDiagnostics diagnostics = OverlayRenderDiagnostics.Empty;
    private DateTimeOffset nextDiagnosticsPublishAt = DateTimeOffset.MinValue;

    public NameplateCardRenderer(
        IObjectTable objectTable,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        OverlayStateStore stateStore,
        AlbumArtworkCache artworkCache,
        Func<bool> isEnabled,
        Func<int> remoteDistanceYalms)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.stateStore = stateStore;
        this.artworkCache = artworkCache;
        this.developmentArtwork = textureProvider.GetFromManifestResource(
            typeof(NameplateCardRenderer).Assembly,
            "XIV.fm.Plugin.Assets.DevelopmentCover.png");
        this.isEnabled = isEnabled;
        this.remoteDistanceYalms = remoteDistanceYalms;
    }

    public OverlayRenderDiagnostics Diagnostics => Volatile.Read(ref this.diagnostics);

    public void Draw()
    {
        var snapshot = this.stateStore.Current;
        if (!this.isEnabled())
        {
            this.PublishDiagnostics(snapshot.Cards.Length, 0, 0, 0, 0, null);
            return;
        }

        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer is null || snapshot.Cards.IsEmpty)
        {
            this.PublishDiagnostics(snapshot.Cards.Length, 0, 0, 0, 0, null);
            return;
        }

        var matchedPlayers = 0;
        var inRangePlayers = 0;
        var projectedAnchors = 0;
        var renderedCards = 0;
        float? localNameplateHeightYalms = null;
        var loadedPlayers = this.objectTable.PlayerObjects.OfType<IPlayerCharacter>().ToArray();
        foreach (var card in snapshot.Cards)
        {
            var target = card.IsLocal
                ? localPlayer
                : FindLoadedPlayer(card.Character, loadedPlayers);

            if (target is null)
                continue;

            matchedPlayers++;
            if (!card.IsLocal && !OverlayVisibility.IsRemoteWithinRange(
                    localPlayer.Position,
                    target.Position,
                    this.remoteDistanceYalms()))
            {
                continue;
            }

            inRangePlayers++;
            if (!DalamudNameplateAnchor.TryGetWorldPosition(target, out var worldAnchor))
                continue;
            if (card.IsLocal)
                localNameplateHeightYalms = OverlayAnchor.GetHeightYalms(target.Position, worldAnchor);
            var cardAnchor = OverlayAnchor.AddSafetyHeight(worldAnchor);
            if (!this.gameGui.WorldToScreen(cardAnchor, out var screenAnchor))
                continue;

            projectedAnchors++;
            DrawCard(card, screenAnchor);
            renderedCards++;
        }

        this.PublishDiagnostics(
            snapshot.Cards.Length,
            matchedPlayers,
            inRangePlayers,
            projectedAnchors,
            renderedCards,
            localNameplateHeightYalms);
    }

    private static IPlayerCharacter? FindLoadedPlayer(
        CharacterIdentity character,
        IReadOnlyList<IPlayerCharacter> loadedPlayers)
    {
        foreach (var player in loadedPlayers)
        {
            if (character.Matches(DalamudCharacterIdentity.From(player)))
                return player;
        }

        return null;
    }

    private void DrawCard(OverlayCard card, Vector2 screenAnchor)
    {
        var scale = ImGuiHelpers.GlobalScale * CardDisplayScale;
        var cardSize = DesignCardSize * scale;

        // The projected point already includes the world-space nameplate safety height.
        ImGui.SetNextWindowPos(screenAnchor, ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowSize(cardSize, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        var windowId = $"XIV.fm card###XIV.fm.Card.{card.Character.Name}.{card.Character.HomeWorldId}";
        try
        {
            var shouldDrawContents = ImGui.Begin(windowId, CardWindowFlags);
            try
            {
                if (!shouldDrawContents)
                    return;

                var origin = ImGui.GetCursorScreenPos();
                var drawList = ImGui.GetWindowDrawList();
                var cardMaximum = origin + cardSize;
                drawList.AddRectFilled(
                    origin,
                    cardMaximum,
                    ImGui.GetColorU32(new Vector4(0.494f, 0.494f, 0.494f, 0.6f)),
                    12f * scale);

                var artworkMinimum = origin + (DesignArtworkOffset * scale);
                var artworkMaximum = artworkMinimum + (DesignArtworkSize * scale);
                if (this.TryGetArtwork(card, out var artwork) && artwork is not null)
                {
                    drawList.AddImage(artwork.Handle, artworkMinimum, artworkMaximum);
                }
                else
                {
                    drawList.AddRectFilled(
                        artworkMinimum,
                        artworkMaximum,
                        ImGui.GetColorU32(new Vector4(1f, 0.251f, 0.251f, 1f)));
                }

                var textMinimum = origin + (DesignTextOffset * scale);
                var textMaximum = new Vector2(
                    cardMaximum.X - (18f * scale),
                    origin.Y + (108f * scale));
                var textColor = ImGui.GetColorU32(Vector4.One);

                drawList.PushClipRect(textMinimum, textMaximum, true);
                drawList.AddText(
                    ImGui.GetFont(),
                    22f * scale,
                    textMinimum,
                    textColor,
                    card.Title);
                drawList.AddText(
                    ImGui.GetFont(),
                    17f * scale,
                    textMinimum + new Vector2(0f, 34f * scale),
                    textColor,
                    card.Artist);
                drawList.PopClipRect();
            }
            finally
            {
                ImGui.End();
            }
        }
        finally
        {
            ImGui.PopStyleVar(2);
        }
    }

    private bool TryGetArtwork(
        OverlayCard card,
        out Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? artwork)
    {
        if (this.artworkCache.TryGet(card.ArtworkUrl, out artwork))
            return true;

        return this.developmentArtwork.TryGetWrap(out artwork, out _);
    }

    private void PublishDiagnostics(
        int requestedCards,
        int matchedPlayers,
        int inRangePlayers,
        int projectedAnchors,
        int renderedCards,
        float? localNameplateHeightYalms)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < this.nextDiagnosticsPublishAt)
            return;

        this.nextDiagnosticsPublishAt = now.AddSeconds(1);
        Interlocked.Exchange(
            ref this.diagnostics,
            new OverlayRenderDiagnostics(
                requestedCards,
                matchedPlayers,
                inRangePlayers,
                projectedAnchors,
                renderedCards,
                now,
                localNameplateHeightYalms));
    }
}
