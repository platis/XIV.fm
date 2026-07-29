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
    private const float MaximumCardWidth = 293f;
    private const float ArtworkSize = 55.2f;
    private const float CardPadding = 6f;
    private const float CardHeight = ArtworkSize + (2f * CardPadding);
    private const float TextGap = 11f;
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
        ImGuiWindowFlags.NoMouseInputs;

    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly OverlayStateStore stateStore;
    private readonly AlbumArtworkCache artworkCache;
    private readonly Dictionary<(string Text, float FontSize, float MaximumWidth), string> textFitCache = [];
    private readonly Func<bool> isEnabled;
    private readonly Func<int> remoteDistanceYalms;
    private OverlayRenderDiagnostics diagnostics = OverlayRenderDiagnostics.Empty;
    private DateTimeOffset nextDiagnosticsPublishAt = DateTimeOffset.MinValue;

    public NameplateCardRenderer(
        IObjectTable objectTable,
        IGameGui gameGui,
        OverlayStateStore stateStore,
        AlbumArtworkCache artworkCache,
        Func<bool> isEnabled,
        Func<int> remoteDistanceYalms)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.stateStore = stateStore;
        this.artworkCache = artworkCache;
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
        var hasArtwork = this.artworkCache.TryGet(card.ArtworkUrl, out var artwork) && artwork is not null;
        var leadingContentWidth = hasArtwork ? ArtworkSize + TextGap : 0f;
        var maximumTextWidth = (MaximumCardWidth - (2f * CardPadding) - leadingContentWidth) * scale;
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
                CardPadding * scale,
                leadingContentWidth * scale,
                titleWidth,
                artistWidth),
            CardHeight * scale);

        // The projected point already includes the world-space nameplate safety height.
        ImGui.SetNextWindowPos(screenAnchor, ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowSize(cardSize, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.82f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(CardPadding * scale));

        var windowId = $"XIV.fm card###XIV.fm.Card.{card.Character.Name}.{card.Character.HomeWorldId}";
        try
        {
            var shouldDrawContents = ImGui.Begin(windowId, CardWindowFlags);
            try
            {
                if (!shouldDrawContents)
                    return;

                var origin = ImGui.GetCursorScreenPos();
                var cardMaximum = ImGui.GetWindowPos() + cardSize;
                var artworkMaximum = origin + new Vector2(ArtworkSize * scale);
                var drawList = ImGui.GetWindowDrawList();
                if (hasArtwork)
                    drawList.AddImage(artwork!.Handle, origin, artworkMaximum);

                var textMinimum = origin + new Vector2(
                    leadingContentWidth * scale,
                    (ArtworkSize - ArtistOffsetY - ArtistFontSize) * scale);
                var textMaximum = new Vector2(
                    cardMaximum.X - (CardPadding * scale),
                    cardMaximum.Y - (CardPadding * scale));
                var titleColor = ImGui.GetColorU32(Vector4.One);
                var artistColor = ImGui.GetColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);

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
            ImGui.PopStyleVar(2);
        }
    }

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
