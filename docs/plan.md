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

_Status: complete; validated in game through v0.1.2_

- [x] Replace direct renderer state with atomic immutable overlay snapshots.
- [x] Define typed character identity with strict name and home-world matching.
- [x] Define location-scope models from typed current-world, territory, map, and instance APIs.
- [x] Support local and remote card instances through one renderer pipeline.
- [x] Add client-side 8-yalm filtering with a configurable 1–20 range for remote players.
- [x] Handle login/logout, typed location changes, object disappearance, projection failure, and plugin disposal.
- [x] Hide all cards while bound by duty and define the same gate for every future server request.
  - v0.1.2 was validated entering and leaving a duty: cards and participation suspend immediately, `/xivfm status` reports the duty state, and cards resume after exit.
- [x] Add developer-only remote mock state.
- [x] Add projected-anchor pipeline diagnostics.

**Exit:** deterministic mock cards follow loaded player characters safely; there is still no production network dependency.

## Phase 2 — contracts and server foundation

_Status: complete; validated through typed client/API integration tests and the disposable durable container stack_

- [x] Add versioned `XIV.fm.Contracts` and an OpenAPI document, including duty-suspended client behavior.
- [x] Scaffold ASP.NET Core API, Application, Domain, and Infrastructure projects.
- [x] Implement structured errors, request IDs, validation, health, readiness, and metrics.
- [x] Add PostgreSQL migrations and Redis adapters through testable ports.
- [x] Add internal installation provisioning, durable rotation/revocation, and route limits.
  - Initial provisioning is reserved for account-link completion; there is no unauthenticated provisioning endpoint.
- [x] Add pinned container infrastructure without exposing public ports.
  - In-process tests cover the typed plugin client, health, authentication, sync, credential lifecycle, validation, snapshot reuse, and structured errors.
  - The disposable stack publishes only a loopback API port and verifies PostgreSQL credential hashes and Redis heartbeat TTLs.

**Exit:** an authenticated test plugin can sync against the local server; no Last.fm or social presence yet.

## Phase 3 — Last.fm linking and local listening

_Status: complete; live provider linking, polling, sync, and in-game local-card rendering validated through v0.1.4_

- [x] Implement short-lived browser/device link sessions with replay protection.
- [x] Complete Last.fm authorization server-side and record the canonical account.
- [x] Discard the Last.fm session key after identity proof while the product is read-only.
- [x] Implement normalized track mapping, one polling stream per active account, cache, single-flight, adaptive 30/90-second scheduling, jitter, backoff, circuit breaking, and a global request budget.
- [x] Return own cached track with freshness/stale metadata through sync.
- [x] Drive the local placeholder card from real linked state.
- [x] Load-test the scheduler and review Last.fm terms/limits before public use.
  - Simulated tests cover 100 worst-case active accounts across 1,000 installation notifications and 200 mixed-use cached accounts.
  - The provider publishes no guaranteed numeric quota. [`lastfm-compliance.md`](lastfm-compliance.md) records the non-commercial, attribution/link, caching, no-artwork, and public-approval constraints.
  - A real Last.fm account completed web authorization through the private tailnet backend; adaptive polling returned the active track through sync and the v0.1.4 plugin rendered it in game.

**Exit:** Private mode provides reliable local listening state without unbounded upstream traffic.

## Phase 4 — public map presence

_Status: complete; automated and disposable durable-stack validation passed_

- [x] Publish only the authenticated user's character and supported location identifiers.
- [x] Build shared time-bounded public snapshots by location scope.
- [x] Add opaque snapshot versioning and snapshot cache metrics.
- [x] Match snapshot identities against loaded player objects.
- [x] Render only matched players within 8 yalms by default.
- [x] Prevent global presence enumeration and reject client-authored track metadata.
- [x] Test crowded maps, duplicate names, world travel, instances, stale presence, and malicious payloads.
  - Automated coverage bounds snapshots at 500 entries, isolates instance/location scopes, removes expired/private publication, reuses versions, rejects forged track fields, retains strict name/home-world matching, and validates plugin snapshot retention/expiry/fail-closed behavior.
  - The disposable PostgreSQL/Redis stack verifies linked Public publication, shared snapshot TTLs, and immediate Private removal without exposing database/cache ports.

**Exit:** multiple clients in one location reuse shared public data and receive correct nearby cards.

## Phase 5 — Custom Relays

_Status: complete; automated and disposable durable-stack validation passed_

- [x] Implement Relay ownership, membership, creation quotas, and deletion.
- [x] Implement hashed, expiring, single-use invitations.
- [x] Implement join, leave, owner kick, removal restrictions, and cache invalidation.
- [x] Implement Custom visibility with membership validation and a maximum selected-Relay count.
- [x] Build shared Relay/location snapshots with authorization on every read.
- [x] Add transactional, authorization, race, quota, and abuse tests.
  - PostgreSQL transactions and unique constraints protect creation, membership, invitation consumption, kicks, leaves, and deletion; memory-mode races and authorization paths have API coverage.
  - Membership revisions are revalidated around every Custom snapshot read, and kick/leave removes Redis publication plus cached Relay/location material immediately.
  - The disposable PostgreSQL/Redis stack verifies hashed invitation persistence and shared Custom snapshots without publishing database/cache ports.

**Exit:** only current Relay members can publish to or read that Relay; kicks take effect immediately.

## Phase 6 — final UX and visual design

_Status: in progress; account-link/settings foundation implemented_

This phase is intentionally collaborative with the product owner.

- [x] Replace placeholder onboarding with account-link setup and clear states.
  - `/xivfm` and Dalamud's configuration button open an Account-first settings window with browser-link progress, failure, duty-suspension, and connected states.
  - Diagnostics exposes explicit loopback/private-test server selection so unreleased server builds can be linked without weakening production URL validation.
- [x] Replace the fixed standing-height card offset with the game's current pose-aware nameplate world position.
  - The read-only native boundary and safety review are recorded in [`ADR 0001`](adr/0001-pose-aware-nameplate-anchor.md); standing, sitting, ground-sitting, lying, mounted, and transformed states still require in-game acceptance.
- [ ] Design Account, Overlay, Privacy, Custom Relays, and Diagnostics settings.
- [ ] Finalize card typography, sizing, animation, accessibility, scale, and obstruction behavior; add album art only if the provider grants permission.
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
- [ ] Obtain and record Last.fm confirmation for the planned public user/request volume; the internal 3.5 requests/second ceiling is not a provider-granted quota.
- [ ] Complete security, privacy, Dalamud policy, dependency, and operational reviews.
- [ ] Run a private alpha, staged beta, and measured release.

**Exit:** production SLOs, privacy, capacity, incident response, and rollback are verified.

## Immediate next steps

1. Extend the successful Phase 3 private-server validation to multiple linked accounts and Phase 4–5 social presence.
2. Begin the collaborative Phase 6 account, privacy, Custom Relay, diagnostics, and card UX work.
3. Keep public rollout blocked on the Last.fm approval and capacity gates recorded for Phase 7.
