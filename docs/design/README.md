# Card design

`card-v1.svg` is the product-owner-provided source for the first approved in-game listening card.

Source measurements:

- Card: 323×127 px
- Corner radius: 12 px
- Artwork: 87×87 px at (18, 19)
- Text origin: approximately (121, 56)
- Background: 60% opacity `#7E7E7E`

The renderer preserves the source proportions and content exactly, then presents the whole card at 70% (approximately 226×89 px at 1× Dalamud UI scale) so it remains restrained in game. It applies Dalamud's global UI scale after that presentation scale and clips the title and artist to the remaining width. The card bottom is projected from a point 0.7 yalms above the game's pose-aware nameplate anchor rather than using a fixed screen-pixel gap.

The embedded development cover remains the fallback while a requested texture is unavailable. Private Last.fm artwork testing is explicitly enabled only on the controlled development backend; public artwork remains blocked under the current compliance review.
