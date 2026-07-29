# Card design

`card-v1.svg` records the current product-owner-directed compact listening card.

Measurements at 1× Dalamud UI scale:

- Card: content-sized up to 293 px wide; fixed 67.2 px height
- Corner radius: 5 px
- Horizontal padding: 9 px
- Vertical padding: 6 px
- Conditional artwork: 55.2×55.2 px (20% larger)
- Artwork-to-text gap: 11 px
- Title: 21.6 px (20% larger), visually bold
- Artist: 19.2 px (20% larger), subdued
- Artist vertical offset from title origin: 23 px
- Background: 82% opacity using the current ImGui window color

The card contracts to the wider of the rendered title and artist lines, including the cover and its gap only when a completed artwork texture is available. Without a cover, text starts at the left padding and no blank artwork area remains. The title and artist fit within the 293 px maximum width, and overlong text is shortened at complete Unicode text-element boundaries with a trailing `...`.

The text block remains bottom-aligned with the artwork. Six pixels of vertical padding keep the cover and artist line clear of the card border, while 9 px horizontal padding protects both artwork and text-only layouts. Enlarging the title while retaining the 23 px line offset reduces its visual gap to the artist.

The card contains no `XIV.fm` heading, separator, or `Last.fm` suffix. The card bottom is projected from a point 0.4 yalms above the game's pose-aware nameplate anchor. Dalamud's global UI scale is applied uniformly.

Private Last.fm artwork testing is explicitly enabled only on the controlled development backend; public artwork remains blocked under the current compliance review.
