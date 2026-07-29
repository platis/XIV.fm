# Last.fm API compliance review

_Status: reviewed 2026-07-19; public rollout remains gated on provider confirmation_

Sources reviewed:

- [Last.fm API Terms of Service](https://www.last.fm/api/tos)
- [Last.fm API introduction](https://www.last.fm/api/intro)
- [`user.getRecentTracks`](https://www.last.fm/api/show/user.getRecentTracks)

This is an engineering review, not legal advice.

## Implemented constraints

- XIV.fm uses Last.fm data only for its non-commercial plugin feature. Commercial use is prohibited unless Last.fm grants a separate agreement.
- The server uses the documented API and does not scrape Last.fm.
- Users explicitly prove control of their Last.fm account. The temporary provider session key is discarded immediately after proof because polling is read-only.
- The plugin attributes listening data to Last.fm. `/xivfm lastfm` opens the current Last.fm track page, or the linked user profile when no track URL is cached.
- Last.fm's reviewed terms expressly exclude images/artwork, so artwork ingestion is disabled by default and remains blocked for public rollout unless Last.fm grants permission. At the product owner's direction, the controlled development backend explicitly sets `XIVFM_LASTFM_ARTWORK_ENABLED=true` to map the largest safe HTTPS image during private and invited external in-game testing. The generic plugin texture pipeline remains bounded regardless of source.
- Listening observations have a 15-minute TTL, no history is retained, and the cache is far below the terms' current 100 MB reasonable-usage cap.
- Polling is cached and account-normalized: installations share one logical stream. To meet the product's overlay-latency target, playing targets 15 seconds, not-playing targets 20 seconds, and offline accounts stop after heartbeat expiry. The shared 3.5 requests/second budget remains authoritative, so provider contention or failure may extend freshness rather than exceed the safety ceiling.
- Every Last.fm request uses the shared 3.5 requests/second token bucket. Durable replicas coordinate the bucket and per-account poll leases through Redis. Backoff and circuit breaking reduce traffic during provider failures.
- XIV.fm does not scrobble, write to Last.fm, sublicense provider data, expose arbitrary-username proxying, or retain a listening history.

## Public-use gate

Last.fm does not publish a guaranteed numeric allowance in the reviewed terms. It reserves discretion to limit request volume or users served and directs higher-volume or commercial users to `partners [at] last [dot] fm`.

The 3.5 requests/second budget is therefore a conservative XIV.fm safety ceiling, not a provider-granted quota. Before public rollout, the owner must contact Last.fm, describe the 100 worst-case / 200 expected linked-user plan, and record written confirmation or lower the configured capacity to the allowance Last.fm provides. Phase 7 retains this external approval gate.

The terms, privacy policy, branding resources, and API response cache headers must be reviewed again immediately before rollout because Last.fm may update them.
