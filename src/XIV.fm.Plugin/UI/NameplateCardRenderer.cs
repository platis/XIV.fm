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
    private const int MaximumTextFitCacheEntries = 256;

    private const ImGuiWindowFlags CardWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoMouseInputs |
        ImGuiWindowFlags.NoBackground;

    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly OverlayStateStore stateStore;
    private readonly AlbumArtworkCache artworkCache;
    private readonly Dictionary<(string Text, float FontSize, float MaximumWidth), string> textFitCache = [];
    private readonly Func<bool> isEnabled;
    private readonly Func<bool> showOwnCard;
    private readonly Func<int> remoteDistanceYalms;
    private readonly Func<float> cardOpacity;
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
        Func<float> cardOpacity)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.stateStore = stateStore;
        this.artworkCache = artworkCache;
        this.isEnabled = isEnabled;
        this.showOwnCard = showOwnCard;
        this.remoteDistanceYalms = remoteDistanceYalms;
        this.cardOpacity = cardOpacity;
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
            if (!OverlayVisibility.ShouldRenderCard(card.IsLocal, this.showOwnCard()))
                continue;

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
            this.DrawCard(card, screenAnchor);
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
        var scale = ImGuiHelpers.GlobalScale;
        var opacity = Math.Clamp(this.cardOpacity(), 0f, 1f);
        var hasArtwork = this.artworkCache.TryGet(card.ArtworkUrl, out var artwork) && artwork is not null;
        var leadingContentWidth = hasArtwork ? ArtworkSize + TextGap : 0f;
        var maximumTextWidth = (MaximumCardWidth - (2f * HorizontalCardPadding) - leadingContentWidth) * scale;
        var artist = card.IsStale ? $"{card.Artist} · cached" : card.Artist;
        var title = this.FitTextWithEllipsis(
            card.Title,
            TitleFontSize * scale,
            maximumTextWidth - (TitleBoldOffsetX * scale));
        artist = this.FitTextWithEllipsis(
            artist,
            ArtistFontSize * scale,
            maximumTextWidth);

        var titleWidth = MeasureTextWidth(title, TitleFontSize * scale) + (TitleBoldOffsetX * scale);
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
        try
        {
            var shouldDrawContents = ImGui.Begin(windowId, CardWindowFlags);
            try
            {
                if (!shouldDrawContents)
                    return;

                var cardMinimum = ImGui.GetWindowPos();
                var cardMaximum = cardMinimum + cardSize;
                var contentMinimum = cardMinimum + new Vector2(
                    HorizontalCardPadding * scale,
                    ((CardHeight - ArtworkSize) / 2f) * scale);
                var artworkMaximum = contentMinimum + new Vector2(ArtworkSize * scale);
                var drawList = ImGui.GetWindowDrawList();
                DrawCardSurface(drawList, cardMinimum, cardMaximum, scale, opacity);
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
                drawList.AddText(
                    ImGui.GetFont(),
                    TitleFontSize * scale,
                    textMinimum,
                    titleColor,
                    title);
                drawList.AddText(
                    ImGui.GetFont(),
                    TitleFontSize * scale,
                    textMinimum + new Vector2(TitleBoldOffsetX * scale, 0f),
                    titleColor,
                    title);
                drawList.AddText(
                    ImGui.GetFont(),
                    ArtistFontSize * scale,
                    textMinimum + new Vector2(0f, ArtistOffsetY * scale),
                    artistColor,
                    artist);
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
        float opacity)
    {
        var rounding = 5f * scale;
        var shadowColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.2f));
        var softShadowColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.09f));
        var borderColor = ImGui.GetColorU32(GetAdaptiveBorderColor());
        var highlightColor = ImGui.GetColorU32(GetAdaptiveHighlightColor());

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

    private string FitTextWithEllipsis(string text, float fontSize, float maximumWidth)
    {
        var key = (text, fontSize, maximumWidth);
        if (this.textFitCache.TryGetValue(key, out var cached))
            return cached;

        if (this.textFitCache.Count >= MaximumTextFitCacheEntries)
            this.textFitCache.Clear();

        var fitted = TextEllipsis.Fit(
            text,
            maximumWidth,
            candidate => MeasureTextWidth(candidate, fontSize));
        this.textFitCache[key] = fitted;
        return fitted;
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
