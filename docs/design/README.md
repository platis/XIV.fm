# Card design

`card-v1.svg` records the current product-owner-directed compact listening card.

Measurements at 1× Dalamud UI scale:

- Card: content-sized up to 293 px wide; fixed 71.2 px height
- Corner radius: 5 px
- Padding: 6 px horizontal and 8 px vertical
- Conditional artwork: 55.2×55.2 px (20% larger), with a 3 px corner radius and subtle adaptive border
- Artwork-to-text gap: 11 px
- Title: 21.6 px (20% larger), visually bold
- Artist: 19.2 px (20% larger), subdued
- Artist vertical offset from title origin: 23 px
- Background: 68.8% opacity using the current ImGui window color (20% less than the previous 86%)
- Surface separation: compact two-layer shadow, adaptive 1 px border, and a restrained top-edge highlight

The card contracts to the wider of the rendered title and artist lines, including the cover and its gap only when a completed artwork texture is available. Without a cover, text starts at the left padding and no blank artwork area remains. The title and artist fit within the 293 px maximum width, and overlong text is shortened at complete Unicode text-element boundaries with a trailing `...`.

The text block remains bottom-aligned with the artwork. The 8 px vertical padding gives the cover and text more breathing room above and below, while 6 px horizontal padding preserves the compact width. Enlarging the title while retaining the 23 px line offset reduces its visual gap to the artist. Title color follows the active ImGui theme; artist color is blended toward the same text color at 68% so hierarchy remains visible without sacrificing contrast.

The surface treatment separates the card from both bright and dark game scenes without adding ornamental content. Its border adapts to the current theme, the top highlight suggests material thickness, and the shadow stays close to the card so it does not read as a large floating panel. Card motion remains coupled directly to the projected nameplate anchor; no smoothing or entrance animation delays that spatial relationship.

The card contains no `XIV.fm` heading, separator, or `Last.fm` suffix. The card bottom is projected from a point 0.4 yalms above the game's pose-aware nameplate anchor. Dalamud's global UI scale is applied uniformly.

Private Last.fm artwork testing is explicitly enabled only on the controlled development backend; public artwork remains blocked under the current compliance review.
