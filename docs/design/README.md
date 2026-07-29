# Card design

`card-v1.svg` records the current product-owner-directed compact listening card.

Measurements at 1× Dalamud UI scale:

- Card: 222×58 px
- Corner radius: 5 px
- Padding: 8 px
- Conditional artwork: 42×42 px at (8, 8)
- Text origin: (60, 9)
- Title: 16 px, visually bold
- Artist/attribution: 13 px, subdued
- Background: 82% opacity using the current ImGui window color

The card reserves the artwork area to keep title placement stable, but draws no development image, colored block, or other placeholder when a cover is unavailable. Long title and artist text are clipped to the card bounds. The red `XIV.fm` heading and separator from the diagnostic placeholder are removed.

The card bottom is projected from a point 0.4 yalms above the game's pose-aware nameplate anchor. Dalamud's global UI scale is applied uniformly.

Private Last.fm artwork testing is explicitly enabled only on the controlled development backend; public artwork remains blocked under the current compliance review.
