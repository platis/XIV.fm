# Card design

`card-v1.svg` is the product-owner-provided source for the first approved in-game listening card.

Source measurements:

- Card: 323×127 px
- Corner radius: 12 px
- Artwork: 87×87 px at (18, 19)
- Text origin: approximately (121, 56)
- Background: 60% opacity `#7E7E7E`

The source remains retained unchanged. During the current listening-state diagnosis, the in-game renderer has temporarily returned to the earlier compact placeholder-card layout so dynamic title/artist delivery can be evaluated without the custom artwork drawing path. Its bottom is projected from a point 0.2 yalms above the game's pose-aware nameplate anchor rather than using a fixed screen-pixel gap.

The bounded artwork loader remains available but its textures are not drawn by the temporary diagnostic card. Private Last.fm artwork testing is explicitly enabled only on the controlled development backend; public artwork remains blocked under the current compliance review.
