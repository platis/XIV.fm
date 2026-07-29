# Card design

`card-v1.svg` records the current product-owner-directed compact listening card.

Measurements at 1× Dalamud UI scale:

- Card: content-sized up to 297 px wide; fixed 75.2 px height
- Corner radius: 5 px
- Padding: 10 px on every outer edge
- Conditional artwork: 55.2×55.2 px (20% larger), with a 3 px corner radius and subtle adaptive border
- Artwork-to-text gap: 10 px
- Title: 21.6 px (20% larger), visually bold
- Artist: 19.2 px (20% larger), subdued
- Artist vertical offset from title origin: 23 px
- Background: FFXIV-style charcoal gray (`#2B2B2B`) with configurable 0–100% opacity, defaulting to 60%
- Display scale: independently configurable from 50–150% for the local card and other players’ cards, defaulting to 100% for both
- Surface separation: compact two-layer shadow, adaptive 1 px border, and a restrained top-edge highlight

The card contracts to the wider of the rendered title and artist lines, including the cover and its gap only when a completed artwork texture is available. Without a cover, text starts at the left padding and no blank artwork area remains. The title and artist fit within the 297 px maximum width, and overlong text is shortened at complete Unicode text-element boundaries with a trailing `...`.

Artwork is positioned from the card bounds rather than ImGui's cursor and explicitly centered between the 10 px top and bottom edges. The text block is then bottom-aligned to the centered artwork. Uniform 10 px outer padding keeps every card edge balanced, while the 10 px artwork-to-text gap preserves clear internal separation. Enlarging the title while retaining the 23 px line offset reduces its visual gap to the artist. Title color follows the active ImGui theme; artist color is blended toward the same text color at 68% so hierarchy remains visible without sacrificing contrast.

The default 60%-opaque FFXIV-style charcoal surface separates the card from both bright and dark game scenes without reading as black or adding ornamental content. Overlay settings can adjust only the background surface opacity from 0–100%; text and artwork remain fully opaque for legibility. Its border adapts to the surface, the top highlight suggests material thickness, and the shadow stays close to the card so it does not read as a large floating panel. Card motion remains coupled directly to the projected nameplate anchor; no smoothing or entrance animation delays that spatial relationship.

The card exists only for a synchronized `playing` observation with track metadata. Nothing is rendered for not-playing, unavailable, or unlinked state; there is no status placeholder. The card contains no `XIV.fm` heading, separator, or `Last.fm` suffix. The card bottom is projected from a point 0.4 yalms above the game's pose-aware nameplate anchor. Dalamud's global UI scale is applied uniformly, then the appropriate local/other-player size setting scales the complete card without changing its proportions.

Last.fm artwork testing is explicitly enabled only on the controlled development backend for private or invited external testing at the product owner's direction; broader public artwork rollout remains blocked under the current compliance review.

`enable-icon.svg` records the product-owner-supplied concept for the server-info-bar shortcut. Dalamud's native DTR entry accepts game text and bitmap-font icons rather than custom SVG textures, so the runtime uses the compact game-font label `.FM`. It inherits the server-info bar’s default yellow while cards are visible and renders red while hidden.
