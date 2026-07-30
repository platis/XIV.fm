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
    private const float MaximumCardWidth = 297f;
    private const float ArtworkSize = 55.2f;
    private const float HorizontalCardPadding = 10f;
    private const float VerticalCardPadding = 10f;
    private const float CardHeight = ArtworkSize + (2f * VerticalCardPadding);
    private const float TextGap = 10f;
    private const float TitleFontSize = 21.6f;
    private const float ArtistFontSize = 19.2f;
    private const float ArtistOffsetY = 23f;
    private const float TitleBoldOffsetX = 0.78f;
    private const int MaximumMarqueeStateEntries = 512;

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
    private readonly Dictionary<
        (CharacterIdentity Character, string TrackTitle, string TrackArtist, string Text),
        double> marqueeStartedAt = [];
    private readonly Func<bool> isEnabled;
    private readonly Func<bool> showOwnCard;
    private readonly Func<int> remoteDistanceYalms;
    private readonly Func<float> cardOpacity;
    private readonly Func<float> ownCardScale;
    private readonly Func<float> otherCardScale;
    private readonly Func<bool> isInteractionModifierHeld;
    private readonly Action<Uri> openTrackLink;
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
        Func<float> cardOpacity,
        Func<float> ownCardScale,
        Func<float> otherCardScale,
        Func<bool> isInteractionModifierHeld,
        Action<Uri> openTrackLink)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.stateStore = stateStore;
        this.artworkCache = artworkCache;
        this.isEnabled = isEnabled;
        this.showOwnCard = showOwnCard;
        this.remoteDistanceYalms = remoteDistanceYalms;
        this.cardOpacity = cardOpacity;
        this.ownCardScale = ownCardScale;
        this.otherCardScale = otherCardScale;
        this.isInteractionModifierHeld = isInteractionModifierHeld;
        this.openTrackLink = openTrackLink;
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
        for (var layer = 0; layer < OverlayCardStacking.LayerCount; layer++)
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
                this.DrawCard(
                    card,
                    screenAnchor,
                    distanceYalms,
                    maximumRemoteDistanceYalms,
                    interactionModifierHeld);
                renderedCards++;
            }
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

    private void DrawCard(
        OverlayCard card,
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
        var opacity = Math.Clamp(this.cardOpacity(), 0f, 1f);
        var hasArtwork = this.artworkCache.TryGet(card.ArtworkUrl, out var artwork) && artwork is not null;
        var leadingContentWidth = hasArtwork ? ArtworkSize + TextGap : 0f;
        var artist = card.IsStale ? $"{card.Artist} · cached" : card.Artist;
        var titleWidth = MeasureTextWidth(card.Title, TitleFontSize * scale) + (TitleBoldOffsetX * scale);
        var artistWidth = MeasureTextWidth(artist, ArtistFontSize * scale);
        var cardSize = new Vector2(
            ContentSizedCardWidth.Calculate(
                MaximumCardWidth * scale,
                HorizontalCardPadding * scale,
                leadingContentWidth * scale,
                titleWidth,
                artistWidth),
            CardHeight * scale);

        // The projected point already includes the world-space nameplate safety height.
        ImGui.SetNextWindowPos(screenAnchor, ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowSize(cardSize, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f * scale);
        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowPadding,
            new Vector2(HorizontalCardPadding * scale, VerticalCardPadding * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        var windowId = $"XIV.fm card###XIV.fm.Card.{card.Character.Name}.{card.Character.HomeWorldId}";
        var isInteractive = interactionModifierHeld && card.TrackUrl is not null;
        var windowFlags = isInteractive
            ? CardWindowFlags
            : CardWindowFlags | ImGuiWindowFlags.NoMouseInputs;
        try
        {
            var shouldDrawContents = ImGui.Begin(windowId, windowFlags);
            try
            {
                // Reorder only the display list, not focus, so the local card stays above
                // overlapping remote cards without stealing input from another window.
                if (card.IsLocal)
                    ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());

                if (!shouldDrawContents)
                    return;

                var cardMinimum = ImGui.GetWindowPos();
                var cardMaximum = cardMinimum + cardSize;
                var contentMinimum = cardMinimum + new Vector2(
                    HorizontalCardPadding * scale,
                    ((CardHeight - ArtworkSize) / 2f) * scale);
                var artworkMaximum = contentMinimum + new Vector2(ArtworkSize * scale);
                var drawList = ImGui.GetWindowDrawList();
                var isHovered = false;
                if (isInteractive)
                {
                    ImGui.SetCursorScreenPos(cardMinimum);
                    if (ImGui.InvisibleButton("Open track on Last.fm", cardSize))
                        this.openTrackLink(card.TrackUrl!);
                    isHovered = ImGui.IsItemHovered();
                    if (isHovered)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        ImGui.SetTooltip("Open track on Last.fm");
                    }
                }

                DrawCardSurface(drawList, cardMinimum, cardMaximum, scale, opacity, isHovered);
                if (hasArtwork)
                {
                    var artworkRounding = 3f * scale;
                    drawList.AddImageRounded(
                        artwork!.Handle,
                        contentMinimum,
                        artworkMaximum,
                        Vector2.Zero,
                        Vector2.One,
                        uint.MaxValue,
                        artworkRounding,
                        ImDrawFlags.RoundCornersAll);
                    drawList.AddRect(
                        contentMinimum,
                        artworkMaximum,
                        ImGui.GetColorU32(GetAdaptiveBorderColor()),
                        artworkRounding);
                }

                var textMinimum = contentMinimum + new Vector2(
                    leadingContentWidth * scale,
                    (ArtworkSize - ArtistOffsetY - ArtistFontSize) * scale);
                var textMaximum = new Vector2(
                    cardMaximum.X - (HorizontalCardPadding * scale),
                    artworkMaximum.Y);
                var style = ImGui.GetStyle();
                var surfaceColor = GetSurfaceColor(opacity);
                var titleColor = ImGui.GetColorU32(style.Colors[(int)ImGuiCol.Text]);
                var artistColorValue = Vector4.Lerp(surfaceColor, style.Colors[(int)ImGuiCol.Text], 0.68f);
                artistColorValue.W = style.Colors[(int)ImGuiCol.Text].W;
                var artistColor = ImGui.GetColorU32(artistColorValue);

                drawList.PushClipRect(textMinimum, textMaximum, true);
                this.DrawMarqueeText(
                    drawList,
                    card,
                    card.Title,
                    TitleFontSize * scale,
                    textMinimum,
                    textMaximum.X,
                    titleColor,
                    TitleBoldOffsetX * scale,
                    scale);
                DrawEllipsizedText(
                    drawList,
                    artist,
                    ArtistFontSize * scale,
                    textMinimum + new Vector2(0f, ArtistOffsetY * scale),
                    textMaximum.X,
                    artistColor);
                drawList.PopClipRect();
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
    }

    private static void DrawCardSurface(
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 maximum,
        float scale,
        float opacity,
        bool isHovered)
    {
        var rounding = 5f * scale;
        var shadowColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.2f));
        var softShadowColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.09f));
        var borderColor = ImGui.GetColorU32(isHovered
            ? new Vector4(0.88f, 0.23f, 0.36f, 0.9f)
            : GetAdaptiveBorderColor());
        var highlightColor = ImGui.GetColorU32(isHovered
            ? new Vector4(1f, 1f, 1f, 0.22f)
            : GetAdaptiveHighlightColor());

        drawList.PushClipRectFullScreen();
        drawList.AddRectFilled(
            minimum + new Vector2(-1f * scale, 1f * scale),
            maximum + new Vector2(1f * scale, 4f * scale),
            softShadowColor,
            rounding + (2f * scale));
        drawList.AddRectFilled(
            minimum + new Vector2(0f, 1f * scale),
            maximum + new Vector2(0f, 3f * scale),
            shadowColor,
            rounding + scale);
        drawList.AddRectFilled(minimum, maximum, ImGui.GetColorU32(GetSurfaceColor(opacity)), rounding);
        drawList.AddRect(minimum, maximum, borderColor, rounding);
        drawList.AddLine(
            minimum + new Vector2(rounding, scale),
            new Vector2(maximum.X - rounding, minimum.Y + scale),
            highlightColor,
            scale);
        drawList.PopClipRect();
    }

    private static Vector4 GetSurfaceColor(float opacity) => new(0.169f, 0.169f, 0.169f, opacity);

    private static Vector4 GetAdaptiveBorderColor()
    {
        var surface = GetSurfaceColor(1f);
        return GetRelativeLuminance(surface) < 0.5f
            ? new Vector4(1f, 1f, 1f, 0.14f)
            : new Vector4(0f, 0f, 0f, 0.18f);
    }

    private static Vector4 GetAdaptiveHighlightColor()
    {
        var surface = GetSurfaceColor(1f);
        return GetRelativeLuminance(surface) < 0.5f
            ? new Vector4(1f, 1f, 1f, 0.12f)
            : new Vector4(1f, 1f, 1f, 0.28f);
    }

    private static float GetRelativeLuminance(Vector4 color) =>
        (0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z);

    private void DrawMarqueeText(
        ImDrawListPtr drawList,
        OverlayCard card,
        string text,
        float fontSize,
        Vector2 origin,
        float maximumX,
        uint color,
        float boldOffsetX,
        float scale)
    {
        var textWidth = MeasureTextWidth(text, fontSize) + boldOffsetX;
        var availableWidth = MathF.Max(0f, maximumX - origin.X);
        if (!TextMarquee.ShouldScroll(textWidth, availableWidth))
        {
            DrawTextCopy(drawList, text, fontSize, origin, color, boldOffsetX);
            return;
        }

        var key = (card.Character, card.Title, card.Artist, text);
        if (!this.marqueeStartedAt.TryGetValue(key, out var startedAt))
        {
            if (this.marqueeStartedAt.Count >= MaximumMarqueeStateEntries)
                this.marqueeStartedAt.Clear();

            startedAt = ImGui.GetTime();
            this.marqueeStartedAt[key] = startedAt;
        }

        // Keep the animation phase in scale-independent card coordinates. Remote
        // distance scaling can then change every frame without changing the cycle.
        var gap = TextMarquee.GapPixels * scale;
        var unscaledTextWidth = textWidth / scale;
        var offset = TextMarquee.CalculateScaledOffset(
            ImGui.GetTime() - startedAt,
            unscaledTextWidth,
            scale);
        var firstOrigin = origin - new Vector2(offset, 0f);
        DrawTextCopy(drawList, text, fontSize, firstOrigin, color, boldOffsetX);
        DrawTextCopy(
            drawList,
            text,
            fontSize,
            firstOrigin + new Vector2(textWidth + gap, 0f),
            color,
            boldOffsetX);
    }

    private static void DrawEllipsizedText(
        ImDrawListPtr drawList,
        string text,
        float fontSize,
        Vector2 origin,
        float maximumX,
        uint color)
    {
        var availableWidth = MathF.Max(0f, maximumX - origin.X);
        var fittedText = TextEllipsis.Fit(
            text,
            availableWidth,
            candidate => MeasureTextWidth(candidate, fontSize));
        DrawTextCopy(drawList, fittedText, fontSize, origin, color, 0f);
    }

    private static void DrawTextCopy(
        ImDrawListPtr drawList,
        string text,
        float fontSize,
        Vector2 origin,
        uint color,
        float boldOffsetX)
    {
        drawList.AddText(ImGui.GetFont(), fontSize, origin, color, text);
        if (boldOffsetX > 0f)
            drawList.AddText(ImGui.GetFont(), fontSize, origin + new Vector2(boldOffsetX, 0f), color, text);
    }

    private static float MeasureTextWidth(string text, float fontSize)
    {
        var currentFontSize = ImGui.GetFontSize();
        if (currentFontSize <= 0f)
            return 0f;

        return ImGui.CalcTextSize(text).X * (fontSize / currentFontSize);
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
