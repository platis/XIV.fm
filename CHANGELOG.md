# Changelog

All notable XIV.fm changes are documented here. Development builds remain prereleases until the plugin and server reach their stable acceptance criteria.

## Unreleased

## [0.1.27] - 2026-07-29

### Added

- Add independent 50–150% size controls for the local listening card and other players’ cards.

### Changed

- Replace the server-info-bar `[FM]` shortcut with `.FM`, using the bar’s default yellow while cards are visible and red while hidden.

## [0.1.26] - 2026-07-29

### Changed

- Move Custom Relay audience selection into Privacy, where choosing a Relay now activates Custom visibility and controls presence sharing in both directions.
- Streamline Relay actions with direct Join, same-row Join/Create/Rename controls, and no invitation preview or management reload button.

## [0.1.25] - 2026-07-29

### Fixed

- Fix plugin startup when fewer than five Custom Relay audiences are saved, including the default empty selection.

## [0.1.24] - 2026-07-29

### Added

- Add complete Custom Relay interaction to the plugin: create and join Relays, preview invitations, select up to five publication audiences, and manage owned Relay names, invitations, members, and deletion.
- Add a duty-gated typed Relay API client and persist bounded Custom Relay audience selections for authenticated sync.

## [0.1.23] - 2026-07-29

### Changed

- Replace the server-bar Bard icon with a compact `[FM]` shortcut that is green while cards are visible and gold while hidden.
- Reduce the settings window bounds by 30% and make choice cards and account actions adapt to the narrower layout.
- Add a same-row Overlay option to hide only the local player's listening card while leaving nearby cards visible.
- Increase listening-card outer padding from 8 to 10 px and maximum width from 293 to 297 px so the added padding does not reduce content space.

## [0.1.22] - 2026-07-29

### Changed

- Move the Last.fm disconnect action onto the connected-account action row and give it a lower-emphasis outlined danger treatment.

### Fixed

- Replace the unsupported Unicode music note in FFXIV's server info bar with the game's built-in Bard bitmap icon so the shortcut renders reliably.

## [0.1.21] - 2026-07-29

### Added

- Add a confirmed Last.fm disconnect action that revokes the current XIV.fm installation, removes its active presence, and leaves the user's Last.fm account and history unchanged.
- Add a brief first-run welcome that explains XIV.fm in plain language and offers direct Last.fm linking, a GitHub link, and a skip option.
- Add a music-note shortcut to FFXIV's native server info bar: left-click it to show or hide listening cards, or right-click it to open XIV.fm settings.

### Changed

- Add a 0–100% card-background opacity control to Overlay settings, defaulting to 60% without fading text or artwork.
- Refine the Phase 6 settings design with a cohesive FFXIV-style charcoal surface system, consistent spacing and rounding, clearer Overlay navigation, and unified Account, Privacy, Relay, and Diagnostics states.

## [0.1.20] - 2026-07-29

### Changed

- Center album artwork explicitly, bottom-align text to it, use uniform 8 px outer padding, and refine the card to a 61.92%-opaque FFXIV-style charcoal surface.

## [0.1.19] - 2026-07-29

### Changed

- Complete the listening-card design refinement with 10 px horizontal padding, 8 px vertical padding, and a matching 10 px artwork-to-text gap.

## [0.1.18] - 2026-07-29

### Fixed

- Reduce healthy Last.fm-to-local-overlay propagation from a worst case of roughly two minutes to a sub-30-second target by polling active accounts every 15–20 seconds and syncing cached state every 5 seconds.
- Match the album-art-to-text gap to the card's 6 px horizontal padding for even spacing.

## [0.1.17] - 2026-07-29

### Changed

- Increase listening-card vertical padding from 6 to 8 px and reduce its surface opacity by 20%, from 86% to 68.8%.

## [0.1.16] - 2026-07-29

### Changed

- Refine listening cards with a layered compact shadow, adaptive border and top highlight, rounded artwork, and theme-aware title and artist contrast while preserving the approved dimensions and content layout.

## [0.1.15] - 2026-07-29

### Changed

- Refine the settings window with clearer hierarchy, polished status surfaces, direct privacy choices, and focused account, card, Relay, and diagnostics copy.
- Make disabled and development-only controls reflect their active state, add a one-click card-distance reset, and simplify render diagnostics for in-game troubleshooting.

## [0.1.14] - 2026-07-29

### Fixed

- Make the listening card's horizontal and vertical border padding uniformly 6 px.

## [0.1.13] - 2026-07-29

### Fixed

- Restore visible border padding around enlarged cover artwork and the bottom-aligned text block.

## [0.1.12] - 2026-07-29

### Changed

- Size listening cards dynamically to their text up to 293 px wide, reduce their fixed height by 10%, and remove the reserved artwork gap when no cover is available.
- Increase cover artwork, track titles, and artist names by 20%, while tightening the title-to-artist spacing.

## [0.1.11] - 2026-07-29

### Changed

- Widen the compact card by 20% to 293×64 px, bottom-align its text with the cover artwork, and increase the artist text to 16 px.

## [0.1.10] - 2026-07-29

### Changed

- Increase the compact card by approximately 10% to 244×64 px, remove `Last.fm` from its artist line, and slightly tighten the title-to-artist gap.
- End overlong track and artist names with `...` instead of clipping them abruptly.

## [0.1.9] - 2026-07-29

### Changed

- Refine the compact card into a 222×58 layout with a reserved 42×42 artwork area, no fallback image when artwork is unavailable, a visually bold track title, and no red `XIV.fm` heading or separator.
- Raise the world-space nameplate safety height from 0.2 to 0.4 yalms based on in-game placement feedback.

## [0.1.8] - 2026-07-29

### Changed

- Temporarily return the in-game renderer to the compact placeholder-card layout so listening-state delivery can be tested independently from the artwork renderer.
- Reduce the world-space nameplate safety height from 0.7 to 0.2 yalms after in-game feedback that the card sat too high.

## [0.1.7] - 2026-07-29

### Changed

- Preserve the supplied SVG layout while presenting it uniformly at 70% in game, and anchor its bottom 0.7 yalms above the pose-aware nameplate point so it does not cover player names or titles.
- Keep card copy to the supplied title and artist fields instead of appending status text that changes the approved design.

### Fixed

- Refresh the immutable card snapshot immediately when synced listening state changes, retire covers that are no longer referenced, and retry transient artwork-load failures with bounded backoff so tracks and covers do not remain stuck.

## [0.1.6] - 2026-07-29

### Added

- Implement the approved 323×127 listening card with an 87×87 texture-backed artwork area, clipped title/artist attribution, UI scaling, and the uploaded SVG retained as the design source.
- Add duty-aware, bounded background artwork preparation with HTTPS/local-network validation, raster content checks, a 2 MiB response limit, bounded concurrency/cache cardinality, and an embedded development cover for in-game testing.
- Add a default-off server setting that maps the largest safe HTTPS Last.fm image for the controlled private card test.

### Security

- Keep Last.fm artwork ingestion disabled by default and block its public rollout under the reviewed provider terms unless permission is recorded.

## [0.1.5] - 2026-07-19

### Added

- Follow the game's current pose-aware nameplate world position instead of assuming a fixed standing height, with anchor-height diagnostics for in-game validation.

### Fixed

- Reduce visible track-change latency by syncing fresh playing state from the XIV.fm cache every 10 seconds without increasing Last.fm polling.
- Use Last.fm web authorization so browser approval redirects to XIV.fm; the previous flow incorrectly mixed a pre-issued desktop token with callback polling and remained pending.

## [0.1.4] - 2026-07-19

### Added

- Short-lived, replay-protected Last.fm browser-link sessions with bounded provider calls and a shared 3.5-request/second budget.
- Server-side Last.fm ownership proof, normalized canonical account persistence, and installation credentials issued only after successful proof.
- PostgreSQL account/link-session migration and in-memory integration adapters.
- Normalized `user.getRecentTracks` mapping, adaptive background polling, Redis listening cache and poll leases, bounded jitter/backoff/circuit behavior, and cached listening freshness through sync.
- Duty-gated `/xivfm link` browser authorization, typed plugin link polling, persisted installation credentials, real local listening cards, stale-cache indication, and `/xivfm lastfm` provider links.
- Label-free listening cache/poll/lease metrics and planner load tests for 100 worst-case and 200 mixed-use linked accounts.
- A documented Last.fm terms review with provider artwork disabled and public rollout gated on written volume confirmation.
- Linked-account-only Public visibility, shared bounded location snapshots with opaque versions, Redis output caching, publication invalidation, and label-free snapshot metrics.
- Plugin Private/Public controls and server snapshot cards matched by name/home world and filtered locally to the configured 8-yalm default.
- Custom Relay ownership, membership, soft deletion, idempotent creation, persistent rolling quotas, and owner/member authorization.
- Hashed, expiring, revocable, atomically single-use invitations with kick/rejoin restrictions.
- Membership-authorized Custom sync, shared revision-keyed Relay/location snapshots, and immediate join/leave/kick cache and publication invalidation.
- PostgreSQL Relay migration, Redis Relay presence/snapshot adapters, OpenAPI contracts, and transactional/race/quota/abuse integration coverage.
- Account-first Dalamud settings with discoverable Last.fm browser linking, live link/sync states, overlay and privacy controls, diagnostics, and private-test server configuration.

### Security

- Hash link credentials, callback state, and provider request tokens at rest; atomically consume browser callbacks and discard Last.fm session keys immediately after identity proof.
- Keep listening lookups out of sync handlers and enforce a distributed Redis 3.5-request/second provider budget.
- Store invitation secrets only as SHA-256 hashes, revalidate membership revisions around every Custom snapshot read, and fail closed after removal.

## [0.1.3] - 2026-07-19

### Added

- Frozen v1 sync request, response, snapshot, listening-state, visibility, and structured-error contracts.
- OpenAPI 3.1 documentation and wire-format compatibility tests.
- ASP.NET Core API, Application, Domain, and Infrastructure module foundations.
- Authenticated sync with hashed opaque credentials, rotation/revocation, bounded validation, structured errors, request IDs, rate limits, health checks, and metrics instrumentation.
- PostgreSQL credential migrations and Redis expiring-presence adapters.
- Pinned non-root server image and a disposable container integration stack with loopback-only API ingress and no database/cache host ports.
- Typed bounded plugin HTTP client and one cancellable duty-aware development sync coordinator.
- Integration tests for typed plugin/server sync, authentication, credential lifecycle, heartbeat storage, snapshot reuse, validation, and failure behavior.

## [0.1.2] - 2026-07-19

### Added

- Hide all cards while bound by duty and expose the same policy for future server-request gating.
- Report duty suspension in `/xivfm status` diagnostics.

## [0.1.1] - 2026-07-19

### Fixed

- Defer initial object-table access to Dalamud's framework thread so the plugin can load safely.

## [0.1.0] - 2026-07-18

### Added

- Greenfield .NET 10 and Dalamud API 15 project foundation.
- Placeholder card projected above the local player.
- Developer mock cards for loaded remote players.
- Immutable overlay snapshots and one local/remote rendering path.
- Strict character name and home-world matching.
- Client-side remote distance filtering with an 8-yalm default.
- Typed current-world, territory, map, and instance snapshots.
- Login, logout, location-change, projection, and rendering diagnostics.
- Public GitHub-backed Dalamud custom repository tooling.
