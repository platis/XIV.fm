# XIV.fm development plan

_Status: active_

_Visual direction: placeholder until Phase 6_

## Delivery principles

- Build thin, testable vertical slices.
- Resolve risky provider/game integrations before visual polish.
- Keep the plugin usable in development at every milestone.
- Introduce infrastructure only when a feature requires it.
- Measure upstream traffic and freshness rather than estimating production capacity from request counts alone.

## Phase 0 — repository foundation

- [x] Bootstrap the independent `platis/XIV.fm` workspace.
- [x] Configure exact `XIV.fm` assembly, manifest, and root namespace names.
- [x] Pin .NET 10.0.301 and Dalamud.NET.Sdk 15.0.0.
- [x] Add deterministic builds, package lockfiles, formatting, tests, and a single check command.
- [x] Document product scope, architecture, Custom Relays, limits, and release rules.
- [x] Create a placeholder card projected above the local character.
- [x] Verify the packaged development plugin in FFXIV/Dalamud.
  - v0.1.1 loads successfully, renders the local placeholder, and responds correctly to the `/xivfm` overlay toggle.
- [x] Confirm the placeholder follows real nameplates across representative races, camera distances, and UI scales.
  - The development card renders slightly too high; final vertical placement is deferred to Phase 6 visual design.

**Exit:** CI and local checks pass, and an in-game screenshot/test confirms the anchor behavior.

## Phase 1 — plugin state and anchoring foundation

- [x] Replace direct renderer state with atomic immutable overlay snapshots.
- [x] Define typed character identity with strict name and home-world matching.
- [x] Define location-scope models from typed current-world, territory, map, and instance APIs.
- [x] Support local and remote card instances through one renderer pipeline.
- [x] Add client-side 8-yalm filtering with a configurable 1–20 range for remote players.
- [x] Handle login/logout, typed location changes, object disappearance, projection failure, and plugin disposal.
- [ ] Define and implement the duty rendering policy.
- [x] Add developer-only remote mock state.
- [x] Add projected-anchor pipeline diagnostics.

**Exit:** deterministic mock cards follow loaded player characters safely; there is still no production network dependency.

## Phase 2 — contracts and server foundation

- [ ] Add versioned `XIV.fm.Contracts` and an OpenAPI document.
- [ ] Scaffold ASP.NET Core API, Application, Domain, and Infrastructure projects.
- [ ] Implement structured errors, request IDs, validation, health, readiness, and metrics.
- [ ] Add PostgreSQL migrations and Redis adapters through testable ports.
- [ ] Add opaque installation credentials, hashing, rotation/revocation, and route limits.
- [ ] Add container and integration-test infrastructure without exposing public ports.

**Exit:** an authenticated test plugin can sync against the local server; no Last.fm or social presence yet.

## Phase 3 — Last.fm linking and local listening

- [ ] Implement short-lived browser/device link sessions with replay protection.
- [ ] Complete Last.fm authorization server-side and record the canonical account.
- [ ] Discard the Last.fm session key after identity proof while the product is read-only.
- [ ] Implement normalized track mapping, one polling stream per active account, cache, single-flight, adaptive 30/90-second scheduling, jitter, backoff, circuit breaking, and a global request budget.
- [ ] Return own cached track with freshness/stale metadata through sync.
- [ ] Drive the local placeholder card from real linked state.
- [ ] Load-test the scheduler and confirm Last.fm terms/limits before public use.

**Exit:** Private mode provides reliable local listening state without unbounded upstream traffic.

## Phase 4 — public map presence

- [ ] Publish only the authenticated user's character and supported location identifiers.
- [ ] Build shared time-bounded public snapshots by location scope.
- [ ] Add ETags/versioning and snapshot cache metrics.
- [ ] Match snapshot identities against loaded player objects.
- [ ] Render only matched players within 8 yalms by default.
- [ ] Prevent global presence enumeration and reject client-authored track metadata.
- [ ] Test crowded maps, duplicate names, world travel, instances, stale presence, and malicious payloads.

**Exit:** multiple clients in one location reuse shared public data and receive correct nearby cards.

## Phase 5 — Custom Relays

- [ ] Implement Relay ownership, membership, creation quotas, and deletion.
- [ ] Implement hashed, expiring, single-use invitations.
- [ ] Implement join, leave, owner kick, removal restrictions, and cache invalidation.
- [ ] Implement Custom visibility with membership validation and a maximum selected-Relay count.
- [ ] Build shared Relay/location snapshots with authorization on every read.
- [ ] Add transactional, authorization, race, quota, and abuse tests.

**Exit:** only current Relay members can publish to or read that Relay; kicks take effect immediately.

## Phase 6 — final UX and visual design

This phase is intentionally collaborative with the product owner.

- [ ] Replace placeholder onboarding with account-link setup and clear states.
- [ ] Design Account, Overlay, Privacy, Custom Relays, and Diagnostics settings.
- [ ] Finalize card typography, sizing, album art, animation, accessibility, scale, and obstruction behavior.
- [ ] Add preview tools without mixing them into production state.
- [ ] Test common resolutions, UI scales, character races/heights, crowded cities, duties, login screens, and controller use.
- [ ] Conduct privacy copy and consent review.

**Exit:** behavior is stable and the approved design is implemented without changing core contracts.

## Phase 7 — production readiness and rollout

- [ ] Build a pinned non-root ARM64 image.
- [ ] Add reviewed Compose/Nginx definitions in the infrastructure repository.
- [ ] Configure secrets outside Git, HTTPS, backups, restore tests, alerts, and rollback.
- [ ] Load-test 100 guaranteed worst-case active listeners, 200 expected mixed-use linked users, and at least 1,000 concurrent plugin sessions.
- [ ] Validate that the global Last.fm budget cannot be exceeded under reconnects, retries, or manual actions.
- [ ] Complete security, privacy, Dalamud policy, dependency, and operational reviews.
- [ ] Run a private alpha, staged beta, and measured release.

**Exit:** production SLOs, privacy, capacity, incident response, and rollback are verified.

## Immediate next steps

1. Decide whether cards are hidden in all duties or only selected combat/PvP contexts.
2. Freeze the v1 sync contract using the validated identity, location, lifecycle, and distance behavior.
3. Begin Phase 2 with versioned contracts and the server foundation.
