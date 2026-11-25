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
    private readonly List<(int Index, IPlayerCharacter Target, float DistanceYalms)> nearbyCards = [];
    private readonly LoadedPlayerIdentities identities = new();
    private readonly NearbyArtworkTracker nearbyArtwork;
    private RemoteCardLookup lookup = RemoteCardLookup.Empty;
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
        NearbyArtworkTracker nearbyArtwork,
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
        this.nearbyArtwork = nearbyArtwork;
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
        var now = DateTimeOffset.UtcNow;
        this.remoteCardWindows.Clear();
        ImGuiWindowPtr? localCardWindow = null;

        this.FindNearbyCards(localPlayer, snapshot, maximumRemoteDistanceYalms, now, out matchedPlayers);
        inRangePlayers = this.nearbyCards.Count;
        foreach (var (index, target, distanceYalms) in this.nearbyCards)
        {
            var cardWindow = this.TryDrawCard(
                snapshot.Cards[index],
                target,
                distanceYalms,
                maximumRemoteDistanceYalms,
                interactionModifierHeld,
                ref projectedAnchors,
                ref localNameplateHeightYalms);
            if (cardWindow is not { } window)
                continue;
            this.remoteCardWindows.Add(window);
            renderedCards++;
        }

        // The local card is drawn last so it stays above every remote card.
        var localCard = snapshot.Cards.FirstOrDefault(static card => card.IsLocal);
        if (localCard is not null && OverlayVisibility.ShouldRenderCard(true, this.showOwnCard()))
        {
            matchedPlayers++;
            inRangePlayers++;
            localCardWindow = this.TryDrawCard(
                localCard,
                localPlayer,
                0f,
                maximumRemoteDistanceYalms,
                interactionModifierHeld,
                ref projectedAnchors,
                ref localNameplateHeightYalms);
            if (localCardWindow is not null)
                renderedCards++;
        }

        // Game objects must not be kept beyond the frame that read them.
        this.nearbyCards.Clear();
        ArrangeCardWindowStack(localCardWindow, this.remoteCardWindows);

        this.PublishDiagnostics(
            snapshot.Cards.Length,
            matchedPlayers,
            inRangePlayers,
            projectedAnchors,
            renderedCards,
            localNameplateHeightYalms);
    }

    private void FindNearbyCards(
        IPlayerCharacter localPlayer,
        OverlaySnapshot snapshot,
        int maximumRemoteDistanceYalms,
        DateTimeOffset now,
        out int matchedPlayers)
    {
        matchedPlayers = 0;
        this.nearbyCards.Clear();
        if (!ReferenceEquals(this.lookup.Snapshot, snapshot))
            this.lookup = new RemoteCardLookup(snapshot);
        if (this.lookup.IsEmpty)
            return;

        this.identities.BeginFrame(now);
        var localPosition = localPlayer.Position;
        foreach (var gameObject in this.objectTable.PlayerObjects)
        {
            if (gameObject is not IPlayerCharacter player ||
                this.identities.Get(player.GameObjectId, player, DecodeIdentity) is not { } identity ||
                !this.lookup.TryFind(identity, out var index))
            {
                continue;
            }

            matchedPlayers++;
            var position = player.Position;
            var distanceYalms = Vector3.Distance(localPosition, position);
            if (!float.IsFinite(distanceYalms))
                continue;
            if (distanceYalms <= maximumRemoteDistanceYalms + NearbyArtworkTracker.PreloadYalms &&
                snapshot.Cards[index].ArtworkUrl is { } artworkUrl)
            {
                this.nearbyArtwork.Observe(artworkUrl, now);
            }

            if (OverlayVisibility.IsRemoteWithinRange(localPosition, position, maximumRemoteDistanceYalms))
                this.nearbyCards.Add((index, player, distanceYalms));
        }

        // Snapshot order keeps overlapping cards stacked the same way on every frame.
        this.nearbyCards.Sort(static (first, second) => first.Index.CompareTo(second.Index));
    }

    private static CharacterIdentity? DecodeIdentity(IPlayerCharacter player)
    {
        var name = player.Name.ToString();
        var homeWorldId = player.HomeWorld.RowId;
        return string.IsNullOrWhiteSpace(name) || homeWorldId == 0
            ? null
            : new CharacterIdentity(name, homeWorldId);
    }

    private ImGuiWindowPtr? TryDrawCard(
        OverlayCard card,
        IPlayerCharacter target,
        float distanceYalms,
        int maximumRemoteDistanceYalms,
        bool interactionModifierHeld,
        ref int projectedAnchors,
        ref float? localNameplateHeightYalms)
    {
        if (!DalamudNameplateAnchor.TryGetWorldPosition(target, out var worldAnchor))
            return null;
        if (card.IsLocal)
            localNameplateHeightYalms = OverlayAnchor.GetHeightYalms(target.Position, worldAnchor);
        if (!this.gameGui.WorldToScreen(OverlayAnchor.AddSafetyHeight(worldAnchor), out var screenAnchor))
            return null;

        projectedAnchors++;
        return this.DrawCard(
            card,
            target,
            screenAnchor,
            distanceYalms,
            maximumRemoteDistanceYalms,
            interactionModifierHeld);
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
