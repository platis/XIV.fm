# Card design

`card-v1.svg` records the current product-owner-directed compact listening card.

Measurements at 1× Dalamud UI scale:

- Card: 244×64 px
- Corner radius: 5 px
- Padding: 9 px
- Conditional artwork: 46×46 px at (9, 9)
- Text origin: (66, 10)
- Title: 18 px, visually bold
- Artist: 14 px, subdued
- Artist vertical offset from title origin: 23 px
- Background: 82% opacity using the current ImGui window color

The card reserves the artwork area to keep title placement stable, but draws no development image, colored block, or other placeholder when a cover is unavailable. Overlong title and artist text are shortened at complete Unicode text-element boundaries and end with `...` rather than being cut by the card edge.

The card contains no `XIV.fm` heading, separator, or `Last.fm` suffix. The card bottom is projected from a point 0.4 yalms above the game's pose-aware nameplate anchor. Dalamud's global UI scale is applied uniformly.

Private Last.fm artwork testing is explicitly enabled only on the controlled development backend; public artwork remains blocked under the current compliance review.
