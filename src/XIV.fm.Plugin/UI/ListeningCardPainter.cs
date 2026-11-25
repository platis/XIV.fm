using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Draws one listening card. The overlay and the settings preview share it, so they always match.
/// </summary>
public sealed class ListeningCardPainter
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
    private const float OutlineWidth = 1.1f;

    private static readonly Vector2[] OutlineDirections =
    [
        new(-1f, 0f),
        new(1f, 0f),
        new(0f, -1f),
        new(0f, 1f),
        new(-0.7f, -0.7f),
        new(0.7f, -0.7f),
        new(-0.7f, 0.7f),
        new(0.7f, 0.7f),
    ];

    private readonly Dictionary<
        (CharacterIdentity Character, string TrackTitle, string TrackArtist, string Text),
        double> marqueeStartedAt = [];

    public static Vector2 GetWindowPadding(float scale) =>
        new(HorizontalCardPadding * scale, VerticalCardPadding * scale);

    public static Vector2 Measure(OverlayCard card, bool hasArtwork, float scale)
    {
        var leadingContentWidth = hasArtwork ? ArtworkSize + TextGap : 0f;
        var titleWidth = MeasureTextWidth(card.Title, TitleFontSize * scale) + (TitleBoldOffsetX * scale);
        var artistWidth = MeasureTextWidth(GetArtistText(card), ArtistFontSize * scale);
        return new Vector2(
            ContentSizedCardWidth.Calculate(
                MaximumCardWidth * scale,
                HorizontalCardPadding * scale,
                leadingContentWidth * scale,
                titleWidth,
                artistWidth),
            CardHeight * scale);
    }

    public void Draw(
        ImDrawListPtr drawList,
        OverlayCard card,
        IDalamudTextureWrap? artwork,
        Vector2 cardMinimum,
        Vector2 cardSize,
        float scale,
        float opacity,
        bool outlineText,
        bool isHovered,
        bool clipToWindow)
    {
        var leadingContentWidth = artwork is not null ? ArtworkSize + TextGap : 0f;
        var artist = GetArtistText(card);
        var cardMaximum = cardMinimum + cardSize;
        var contentMinimum = cardMinimum + new Vector2(
            HorizontalCardPadding * scale,
            ((CardHeight - ArtworkSize) / 2f) * scale);
        var artworkMaximum = contentMinimum + new Vector2(ArtworkSize * scale);

        DrawCardSurface(drawList, cardMinimum, cardMaximum, scale, opacity, isHovered, clipToWindow);
        if (artwork is not null)
        {
            var artworkRounding = 3f * scale;
            drawList.AddImageRounded(
                artwork.Handle,
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
        var availableTextWidth = MathF.Max(
            0f,
            cardSize.X - ((2f * HorizontalCardPadding + leadingContentWidth) * scale));
        var style = ImGui.GetStyle();
        var surfaceColor = GetSurfaceColor(opacity);
        var titleColor = ImGui.GetColorU32(style.Colors[(int)ImGuiCol.Text]);
        var artistColorValue = Vector4.Lerp(surfaceColor, style.Colors[(int)ImGuiCol.Text], 0.68f);
        artistColorValue.W = style.Colors[(int)ImGuiCol.Text].W;
        var artistColor = ImGui.GetColorU32(artistColorValue);
        var outlineColor = outlineText
            ? ImGui.GetColorU32(new Vector4(0f, 0f, 0f, CardAppearance.TextOutlineOpacity(opacity)))
            : 0u;

        // The outline may extend one pixel beyond the text area.
        var outlineMargin = new Vector2(outlineText ? OutlineWidth * scale : 0f);
        drawList.PushClipRect(textMinimum - outlineMargin, textMaximum + outlineMargin, true);
        this.DrawMarqueeText(
            drawList,
            card,
            card.Title,
            TitleFontSize * scale,
            textMinimum,
            availableTextWidth,
            titleColor,
            TitleBoldOffsetX * scale,
            scale,
            outlineColor);
        DrawEllipsizedText(
            drawList,
            artist,
            ArtistFontSize * scale,
            textMinimum + new Vector2(0f, ArtistOffsetY * scale),
            availableTextWidth,
            artistColor,
            scale,
            outlineColor);
        drawList.PopClipRect();
    }

    private static string GetArtistText(OverlayCard card) =>
        card.IsStale ? $"{card.Artist} · cached" : card.Artist;

    private static void DrawCardSurface(
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 maximum,
        float scale,
        float opacity,
        bool isHovered,
        bool clipToWindow)
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

        // Overlay shadows extend past the card window; a preview must stay inside its own window.
        if (!clipToWindow)
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
        if (!clipToWindow)
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
        float availableWidth,
        uint color,
        float boldOffsetX,
        float scale,
        uint outlineColor)
    {
        var textWidth = MeasureTextWidth(text, fontSize) + boldOffsetX;
        if (!TextMarquee.ShouldScroll(textWidth, availableWidth))
        {
            DrawTextCopy(drawList, text, fontSize, origin, color, boldOffsetX, scale, outlineColor);
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
        DrawTextCopy(drawList, text, fontSize, firstOrigin, color, boldOffsetX, scale, outlineColor);
        DrawTextCopy(
            drawList,
            text,
            fontSize,
            firstOrigin + new Vector2(textWidth + gap, 0f),
            color,
            boldOffsetX,
            scale,
            outlineColor);
    }

    private static void DrawEllipsizedText(
        ImDrawListPtr drawList,
        string text,
        float fontSize,
        Vector2 origin,
        float availableWidth,
        uint color,
        float scale,
        uint outlineColor)
    {
        var fittedText = TextEllipsis.Fit(
            text,
            availableWidth,
            candidate => MeasureTextWidth(candidate, fontSize));
        DrawTextCopy(drawList, fittedText, fontSize, origin, color, 0f, scale, outlineColor);
    }

    private static void DrawTextCopy(
        ImDrawListPtr drawList,
        string text,
        float fontSize,
        Vector2 origin,
        uint color,
        float boldOffsetX,
        float scale,
        uint outlineColor)
    {
        if ((outlineColor >> 24) != 0)
        {
            foreach (var direction in OutlineDirections)
            {
                drawList.AddText(
                    ImGui.GetFont(),
                    fontSize,
                    origin + (direction * OutlineWidth * scale) + new Vector2(boldOffsetX / 2f, 0f),
                    outlineColor,
                    text);
            }
        }

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
}
