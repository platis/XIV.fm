# XIV.fm architecture

## System shape

XIV.fm is a monorepo with two deployables: a Dalamud plugin and an ASP.NET Core modular monolith.

```text
Dalamud plugin
  ├── typed game-state adapter
  ├── one cancellable sync coordinator
  ├── immutable state snapshots
  └── placeholder/final ImGui presentation
             │ HTTPS
             ▼
XIV.fm server
  ├── account linking
  ├── listening-state scheduler
  ├── public map snapshots
  ├── Custom Relay authorization and snapshots
  ├── PostgreSQL durable repositories
  ├── Redis ephemeral state and distributed coordination
  └── telemetry, health, readiness, and quotas
             │ globally budgeted requests
             ▼
          Last.fm API
```

No module is deployed as an independent microservice.

## Plugin boundaries

`XIV.fm.Contracts` contains versioned transport records shared by the plugin and server. The frozen v1 sync behavior is documented in [`api-v1.md`](api-v1.md) and [`openapi/v1.openapi.json`](openapi/v1.openapi.json). Contracts contain wire shapes, not application behavior or infrastructure dependencies.

`XIV.fm.Plugin.Network` contains the bounded typed HTTP client. It depends only on the v1 contracts and is integration-tested against the real ASP.NET Core endpoint.

`XIV.fm.Plugin.Core` contains behavior that can be tested without Dalamud:

- Sync state machine and timing policy.
- Visibility configuration.
- Immutable state models.
- API/domain mapping.
- Overlay filtering and anchor calculations.

`XIV.fm.Plugin` contains adapters:

- Dalamud lifecycle, commands, configuration, game objects, conditions, and projection.
- ImGui windows and cards.
- Duty-gated browser integration and nullable future texture integration.
- Typed account-link and sync clients.

Rendering reads an immutable snapshot. It does not call the server, Last.fm, storage, or asynchronous loaders directly. The Account-first Dalamud settings window starts the plugin account-link coordinator, which creates and polls replay-protected sessions only outside duties, presents live pending/failure/connected states, persists the promoted installation credential through Dalamud configuration, and opens provider/track links through Dalamud's browser utility. Sync responses atomically update the local listening model consumed by the same snapshot producer as placeholder and mock cards.

A shared duty-participation policy gates both rendering and networking. While Dalamud reports any bound-by-duty condition, the plugin publishes an empty overlay snapshot, renders no cards, cancels in-flight requests where possible, and starts no server request. It does not send a final leave request from inside the duty; short presence TTLs remove stale publication. The future sync coordinator must evaluate this policy before every request and resume only after duty exit.

The initial card uses a world point projected above the local character. Before social cards are accepted, the anchor adapter must be tested against actual nameplates across races, UI scales, camera angles, world travel, crowded locations, and hidden nameplate conditions.

## Server modules

Implemented project boundaries:

```text
XIV.fm.Server.Api             HTTP/authentication/validation
XIV.fm.Server.Application     use cases, scheduling, authorization
XIV.fm.Server.Domain          accounts, tracks, presence, Relays
XIV.fm.Server.Infrastructure  Last.fm, PostgreSQL, Redis, telemetry
XIV.fm.Contracts              versioned transport contracts (implemented)
```

Dependencies point inward. Domain/application code does not depend on HTTP, EF Core, Redis, or Last.fm JSON.

The first server vertical slice authenticates a hashed opaque installation credential, validates and stores a heartbeat, and returns an empty versioned social snapshot. Linked installations receive cached own-listening state without an inline provider lookup; unlinked development installations receive unavailable state. Custom visibility validates every selected Relay and revalidates durable membership revisions around shared snapshot reads. Application ports have in-memory test adapters and durable PostgreSQL/Redis adapters without moving policy into HTTP code.

Phase 3 account linking uses Last.fm web authorization and a ten-minute server-created session with separate high-entropy callback state and link credential. Last.fm creates the provider token only after browser approval; the callback atomically associates its hash with the session and claims the state/token pair once. Successful Last.fm proof records a normalized canonical account and promotes the already-hashed link credential to an installation credential; no unauthenticated path can perform that promotion. Provider session keys are validated inside the Last.fm adapter and never leave it.

The API composition root provides bounded JSON input, stable problem responses, server-controlled request IDs, per-installation sync rate limiting, liveness/readiness checks, and label-free `System.Diagnostics.Metrics` counters. Metrics have no public HTTP endpoint; a private collector/exporter will be configured during deployment work.

## Last.fm scheduling

Only the server polls Last.fm. One normalized linked account has one logical polling stream regardless of installations, maps, viewers, or Relays.

The implemented initial policy, still requiring live-provider and load-test validation, is:

- Active and playing: target every 30 seconds with schedule jitter.
- Active with no current track: target every 90 seconds with schedule jitter.
- Offline: no polling after heartbeat expiry.
- Errors: bounded exponential backoff with full jitter and circuit breaking after repeated failures.
- Global planning budget: 3.5 requests/second until the actual allowance is confirmed.

Every provider request acquires the shared budget. Label-free counters track cache hits/misses, poll successes/failures, and lease contention without account or track dimensions. Durable mode uses a Redis token bucket and per-account expiring poll lease, so API replicas share the budget and only one may poll an account at a time. Memory mode uses equivalent process-local adapters for tests. Server HTTP requests only refresh activity, enqueue scheduler notifications, and read cached state; they never call Last.fm inline for listening data. Redis caches normalized observations for 15 minutes. Sync marks playing observations stale after 60 seconds and not-playing observations stale after 180 seconds while retaining the last usable cache during upstream failure.

## Location snapshots

A `LocationScope` is derived from stable Dalamud-provided identifiers:

```text
currentWorldId + territoryId + instanceOrWardIdWhenAvailable
```

Each linked authenticated client may publish only its own single character/location heartbeat. Unlinked development credentials cannot select Public. Private sync removes prior publication. The server now builds reusable 20-second Public snapshots:

```text
public:{locationScope}
relay:{relayId}:{locationScope}
```

Public sync returns an opaque content version and suppresses the body when the client already has that version. Redis caches one bounded snapshot per exact location; memory mode provides the equivalent test adapter. Publication identity/location/visibility changes invalidate affected snapshots, while unchanged heartbeats reuse them. Label-free metrics count cache hits/misses, builds, entry counts, and fixed Relay lifecycle/audit events without account, Relay, character, or track dimensions. A future conditional HTTP endpoint may expose the same version as an ETag. Relay snapshots require current membership authorization on every read. Each Relay has a durable membership revision included in its cache identity; reads revalidate the revision before responding, while join, leave, kick, and deletion invalidate affected cached material. A kick also strips the Relay from the removed account's live publication.

The plugin retains unchanged snapshots until their server expiry, maps entries into immutable remote cards, matches strict name/home-world identities against loaded player characters, excludes its own local identity, and applies the default 8-yalm distance limit locally. Login, location changes, duty entry, and snapshot expiry clear stale remote cards. Coordinates are not uploaded.

## Account linking

The plugin is a public client and contains no Last.fm secret.

1. Plugin requests a short-lived link session and receives a Last.fm web-authorization URL with an explicit callback.
2. Browser completes Last.fm authorization; session start and plugin status polling make no provider request.
3. Last.fm redirects the browser with a newly issued provider token; the server atomically validates one-time state, hashes the token, and exchanges it under the global budget.
4. Server records the canonical Last.fm account.
5. Plugin receives an opaque, revocable XIV.fm installation credential.

`user.getRecentTracks` does not require the Last.fm session key. The server discards that provider session after ownership proof unless an approved future write feature requires retention.

## Persistence

PostgreSQL stores normalized Last.fm accounts, replay-protected account-link sessions, installation IDs/account associations, Custom Relays, memberships, membership revisions, hashed invitations, removal restrictions, retained creation events, and unique SHA-256 hashes of high-entropy credentials through checked-in EF Core migrations. Soft-deleted Relay rows retain creation/idempotency history for rolling quota enforcement.

Redis stores installation heartbeat/presence records with server-controlled TTLs, account-to-installation publication pointers, Public and revision-keyed Relay/location snapshots, normalized listening cache entries, expiring per-account poll leases, and the distributed Last.fm request budget. Durable membership remains PostgreSQL-authoritative and Redis authorization data is never trusted without revision validation. Listening history is not retained.

Provider artwork is not ingested because the currently reviewed Last.fm terms expressly exclude images/artwork. Cards attribute Last.fm and expose the provider track/profile link through `/xivfm lastfm`. See [`lastfm-compliance.md`](lastfm-compliance.md) for the review and public-use approval gate.

## Security and privacy

- Authenticate every sync, Relay, and mutation operation.
- Do not expose an arbitrary-username Last.fm proxy.
- Never trust client-supplied track metadata.
- Validate bounded request schemas and return stable machine-readable errors.
- Hash installation and invitation credentials at rest.
- Enforce per-account, per-IP, route, and global quotas.
- Return no global player directory.
- Keep metrics free of account, character, Relay, and track labels.
- Keep detailed metrics and administration private.
- Make account deletion and Relay deletion explicit, testable operations.

## Deployment

The approved private test deployment runs the non-root ARM64 API with persistent PostgreSQL and ephemeral Redis. PostgreSQL and Redis stay on an internal network; only the API joins an unexposed egress network for Last.fm HTTPS. No container publishes a host port; Kestrel exposes a bind-mounted Unix socket through private Tailscale Serve HTTPS, with Funnel disabled. Last.fm secrets remain in the mode-0600 live environment outside Git. A future public deployment still requires a domain, Nginx ingress, backups, monitoring, and separate rollout approval.
