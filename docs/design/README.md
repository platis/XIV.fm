# Card design

`card-v1.svg` is the product-owner-provided source for the first approved in-game listening card.

Implementation measurements at 1× Dalamud UI scale:

- Card: 323×127 px
- Corner radius: 12 px
- Artwork: 87×87 px at (18, 19)
- Text origin: approximately (121, 56)
- Background: 60% opacity `#7E7E7E`

The renderer scales these measurements with Dalamud's global UI scale and clips text to the remaining card width. The embedded development cover validates texture rendering without using provider artwork. Live Last.fm covers remain disabled under the current compliance review.
