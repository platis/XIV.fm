# Changelog

All notable XIV.fm changes are documented here. Development builds remain prereleases until the plugin and server reach their stable acceptance criteria.

## Unreleased

### Fixed

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
