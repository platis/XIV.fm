using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
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
    private const ImGuiWindowFlags CardWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoBackground;

    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly OverlayStateStore stateStore;
    private readonly AlbumArtworkCache artworkCache;
    private readonly ListeningCardPainter painter = new();
    private readonly List<ImGuiWindowPtr> remoteCardWindows = [];
    private readonly Func<bool> isEnabled;
    private readonly Func<bool> showOwnCard;
    private readonly Func<int> remoteDistanceYalms;
    private readonly Func<float> ownCardOpacity;
    private readonly Func<float> otherCardOpacity;
    private readonly Func<bool> outlineText;
    private readonly Func<float> ownCardScale;
    private readonly Func<float> otherCardScale;
    private readonly Func<bool> isInteractionModifierHeld;
    private readonly Action<Uri> openTrackLink;
    private readonly Action<CharacterIdentity, string> hidePlayer;
    private OverlayRenderDiagnostics diagnostics = OverlayRenderDiagnostics.Empty;
    private DateTimeOffset nextDiagnosticsPublishAt = DateTimeOffset.MinValue;

    public NameplateCardRenderer(
        IObjectTable objectTable,
        IGameGui gameGui,
        OverlayStateStore stateStore,
        AlbumArtworkCache artworkCache,
        Func<bool> isEnabled,
        Func<bool> showOwnCard,
        Func<int> remoteDistanceYalms,
        Func<float> ownCardOpacity,
        Func<float> otherCardOpacity,
        Func<bool> outlineText,
        Func<float> ownCardScale,
        Func<float> otherCardScale,
        Func<bool> isInteractionModifierHeld,
        Action<Uri> openTrackLink,
        Action<CharacterIdentity, string> hidePlayer)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.stateStore = stateStore;
        this.artworkCache = artworkCache;
        this.isEnabled = isEnabled;
        this.showOwnCard = showOwnCard;
        this.remoteDistanceYalms = remoteDistanceYalms;
        this.ownCardOpacity = ownCardOpacity;
        this.otherCardOpacity = otherCardOpacity;
        this.outlineText = outlineText;
        this.ownCardScale = ownCardScale;
        this.otherCardScale = otherCardScale;
        this.isInteractionModifierHeld = isInteractionModifierHeld;
        this.openTrackLink = openTrackLink;
        this.hidePlayer = hidePlayer;
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
        var interactionModifierHeld = this.isInteractionModifierHeld();
        var maximumRemoteDistanceYalms = this.remoteDistanceYalms();
        var loadedPlayers = this.objectTable.PlayerObjects.OfType<IPlayerCharacter>().ToArray();
        this.remoteCardWindows.Clear();
        ImGuiWindowPtr? localCardWindow = null;
        for (var layer = 0; layer < OverlayCardStacking.CardLayerCount; layer++)
        {
            foreach (var card in snapshot.Cards)
            {
                if (OverlayCardStacking.GetLayer(card.IsLocal) != layer)
                    continue;
                if (!OverlayVisibility.ShouldRenderCard(card.IsLocal, this.showOwnCard()))
                    continue;

                var target = card.IsLocal
                    ? localPlayer
                    : FindLoadedPlayer(card.Character, loadedPlayers);

                if (target is null)
                    continue;

                matchedPlayers++;
                var distanceYalms = 0f;
                if (!card.IsLocal)
                {
                    distanceYalms = Vector3.Distance(localPlayer.Position, target.Position);
                    if (!float.IsFinite(distanceYalms) ||
                        !OverlayVisibility.IsRemoteWithinRange(
                            localPlayer.Position,
                            target.Position,
                            maximumRemoteDistanceYalms))
                    {
                        continue;
                    }
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
                var cardWindow = this.DrawCard(
                    card,
                    target,
                    screenAnchor,
                    distanceYalms,
                    maximumRemoteDistanceYalms,
                    interactionModifierHeld);
                if (card.IsLocal)
                    localCardWindow = cardWindow;
                else
                    this.remoteCardWindows.Add(cardWindow);
                renderedCards++;
            }
        }

        ArrangeCardWindowStack(localCardWindow, this.remoteCardWindows);

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

    private static void ArrangeCardWindowStack(
        ImGuiWindowPtr? localCardWindow,
        List<ImGuiWindowPtr> remoteCardWindows)
    {
        // Move the complete card group behind normal interface windows. Moving the
        // local card first, followed by remotes in reverse render order, leaves the
        // local card above every remote without covering settings or dialogs.
        if (localCardWindow is { } localWindow)
            ImGuiP.BringWindowToDisplayBack(localWindow);

        for (var index = remoteCardWindows.Count - 1; index >= 0; index--)
            ImGuiP.BringWindowToDisplayBack(remoteCardWindows[index]);
    }

    private ImGuiWindowPtr DrawCard(
        OverlayCard card,
        IPlayerCharacter target,
        Vector2 screenAnchor,
        float distanceYalms,
        int maximumRemoteDistanceYalms,
        bool interactionModifierHeld)
    {
        var configuredScale = card.IsLocal
            ? this.ownCardScale()
            : CardAppearance.ScaleForRemoteDistance(
                this.otherCardScale(),
                distanceYalms,
                maximumRemoteDistanceYalms);
        var scale = ImGuiHelpers.GlobalScale * Math.Clamp(configuredScale, 0.5f, 1.5f);
        var opacity = Math.Clamp(card.IsLocal ? this.ownCardOpacity() : this.otherCardOpacity(), 0f, 1f);
        var hasArtwork = this.artworkCache.TryGet(card.ArtworkUrl, out var artwork) && artwork is not null;
        var cardSize = ListeningCardPainter.Measure(card, hasArtwork, scale);

        // The projected point already includes the world-space nameplate safety height.
        ImGui.SetNextWindowPos(screenAnchor, ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowSize(cardSize, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, ListeningCardPainter.GetWindowPadding(scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        var windowId = $"XIV.fm card###XIV.fm.Card.{card.Character.Name}.{card.Character.HomeWorldId}";
        // Mock cards sit on real players and must not change the hidden list.
        var canHide = !card.IsLocal && card.IsLastFm;
        var isInteractive = interactionModifierHeld && (card.TrackUrl is not null || canHide);
        var windowFlags = isInteractive
            ? CardWindowFlags
            : CardWindowFlags | ImGuiWindowFlags.NoMouseInputs;
        ImGuiWindowPtr cardWindow;
        try
        {
            var shouldDrawContents = ImGui.Begin(windowId, windowFlags);
            cardWindow = ImGuiP.GetCurrentWindow();
            try
            {
                if (!shouldDrawContents)
                    return cardWindow;

                var cardMinimum = ImGui.GetWindowPos();
                var isHovered = false;
                if (isInteractive)
                {
                    ImGui.SetCursorScreenPos(cardMinimum);
                    if (ImGui.InvisibleButton("XIV.fm card action", cardSize) && card.TrackUrl is not null)
                        this.openTrackLink(card.TrackUrl);
                    isHovered = ImGui.IsItemHovered();
                    if (canHide && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        this.hidePlayer(
                            DalamudCharacterIdentity.From(target),
                            target.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty);
                    }

                    if (isHovered)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        ImGui.SetTooltip(GetInteractionHint(card.TrackUrl is not null, canHide));
                    }
                }

                this.painter.Draw(
                    ImGui.GetWindowDrawList(),
                    card,
                    artwork,
                    cardMinimum,
                    cardSize,
                    scale,
                    opacity,
                    this.outlineText(),
                    isHovered,
                    clipToWindow: false);
            }
            finally
            {
                ImGui.End();
            }
        }
        finally
        {
            ImGui.PopStyleVar(3);
        }

        return cardWindow;
    }

    private static string GetInteractionHint(bool canOpen, bool canHide) => (canOpen, canHide) switch
    {
        (true, true) => "Click: open track on Last.fm\nRight-click: hide this player’s card",
        (false, true) => "Right-click: hide this player’s card",
        _ => "Open track on Last.fm",
    };

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
