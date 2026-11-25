# XIV.fm architecture

## System shape

XIV.fm is a monorepo with two deployables: a Dalamud plugin and an ASP.NET Core modular monolith.

```text
Dalamud plugin
  ├── typed game-state adapter
  ├── one cancellable sync coordinator
  ├── immutable state snapshots
  └── playing-only ImGui presentation
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
- Duty-gated browser integration and bounded album-art texture preparation.
- Typed account-link and sync clients.

Rendering reads an immutable snapshot. It does not call the server, Last.fm, storage, or asynchronous loaders directly. A separate duty-aware artwork coordinator observes snapshots outside the draw path, validates public HTTPS artwork locations, bounds each response to 2 MiB, permits only common raster media types, limits concurrency and cache cardinality, retires textures no longer present in the current snapshot, retries transient failures with bounded backoff, and creates Dalamud textures asynchronously. The compact renderer reads only completed textures, includes artwork space only when a completed cover is available, otherwise contracts to a text-only layout, and applies independent bounded display scales to local and remote cards. An overflowing title remains clipped to the card, pauses at the leading edge, then moves left through a slow continuous duplicate-text loop; fitting titles do not animate. Overflowing artist text remains still and is shortened with three dots. Remote cards retain full configured scale through 2 yalms and then shrink with smooth distance interpolation toward the selected visibility limit, never below the existing 50% absolute readability floor. Card windows remain mouse-transparent unless Alt is held; interactive cards carry only validated Last.fm HTTPS track URLs, respond immediately on hover, and delegate browser opening outside the renderer. Overlay mapping creates cards only for `playing` observations with a track; `notPlaying`, `unavailable`, and unlinked state produce no local or remote status placeholder. The Account-first Dalamud settings window starts the plugin account-link coordinator, which creates and polls replay-protected sessions only outside duties, presents live pending/failure/connected states, persists the pending authorization URL and promoted installation credential through Dalamud configuration, opens authorization through the operating-system default-browser shell with Dalamud fallback, and keeps manual reopen/copy actions available until linking completes. A separate duty-gated Relay coordinator drives typed bounded create/join/management requests, exposes immutable UI state, and persists at most five selected audience IDs; recurring sync remains the only presence operation. Changed sync listening state requests an immediate immutable overlay capture so title and artwork references advance together rather than waiting for the periodic capture.

A shared duty-participation policy gates both rendering and networking. While Dalamud reports any bound-by-duty condition, the plugin publishes an empty overlay snapshot, renders no cards, cancels in-flight requests where possible, and starts no server request. It does not send a final leave request from inside the duty; short presence TTLs remove stale publication. The future sync coordinator must evaluate this policy before every request and resume only after duty exit.

Cards use the game's pose-aware nameplate world position plus a 0.4-yalm world-space safety height and project it through Dalamud, so sitting, lying, mounted, and other animated states do not inherit a fixed standing-height offset or a camera-dependent pixel gap. The narrowly scoped, read-only FFXIVClientStructs boundary and its safety constraints are recorded in [`ADR 0001`](adr/0001-pose-aware-nameplate-anchor.md). The anchor adapter must be tested against actual nameplates across races, poses, UI scales, camera angles, world travel, crowded locations, and hidden nameplate conditions.

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

The API composition root provides bounded JSON input, stable problem responses, server-controlled request IDs, per-installation sync rate limiting, liveness/readiness checks, and label-free `System.Diagnostics.Metrics` counters. Revoking the current installation also removes its ephemeral presence and invalidates affected Public or Custom snapshots, while leaving the Last.fm account itself untouched. Metrics have no public HTTP endpoint; a private collector/exporter will be configured during deployment work.

## Last.fm scheduling

Only the server polls Last.fm. One normalized linked account has one logical polling stream regardless of installations, maps, viewers, or Relays.

The implemented initial policy, still requiring live-provider and load-test validation, is:

- Active and playing: target every 15 seconds with schedule jitter.
- Active with no current track: target every 20 seconds with schedule jitter.
- Healthy plugin sync: every 5 seconds, keeping the poll-to-local-overlay path below 30 seconds under an uncongested request budget.
- Offline: no polling after heartbeat expiry.
- Errors: bounded exponential backoff with full jitter and circuit breaking after repeated failures.
- Global planning budget: 3.5 requests/second until the actual allowance is confirmed.

Cache, lease, and provider failures back off per account without ending the polling loop, and the hosted service restarts the loop if an unexpected error escapes it.

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

The plugin retains unchanged snapshots until their server expiry plus an eight-second replacement grace, because a shared snapshot can expire at any point between two five-second syncs. It maps entries into immutable remote cards, matches strict name/home-world identities against loaded player characters, excludes its own local identity, and applies the default 8-yalm distance limit locally. Login, location changes, duty entry, and the end of that grace clear stale remote cards. The local player’s listening state becomes unavailable two minutes after the last successful sync. Coordinates are not uploaded.

## Account linking

The plugin is a public client and contains no Last.fm secret.

1. Plugin requests a short-lived link session and receives a Last.fm web-authorization URL with an explicit callback.
2. The plugin asks the operating system to open the authorization URL in the default browser and exposes persistent **Open Last.fm authorization** and **Copy connection link** fallbacks until the browser completes Last.fm authorization; session start and plugin status polling make no provider request.
3. Last.fm redirects the browser with a newly issued provider token; the server atomically validates one-time state, hashes the token, and exchanges it under the global budget.
4. Server records the canonical Last.fm account.
5. Plugin receives an opaque, revocable XIV.fm installation credential.

`user.getRecentTracks` does not require the Last.fm session key. The server discards that provider session after ownership proof unless an approved future write feature requires retention.

## Persistence

Relay changes and account-link completion run in serializable transactions. PostgreSQL may reject one of several concurrent transactions, so each is retried with bounded jittered backoff; an exhausted retry budget returns a state conflict.

PostgreSQL stores normalized Last.fm accounts, replay-protected account-link sessions, installation IDs/account associations, Custom Relays, memberships, membership revisions, hashed invitations, removal restrictions, retained creation events, and unique SHA-256 hashes of high-entropy credentials through checked-in EF Core migrations. Soft-deleted Relay rows retain creation/idempotency history for rolling quota enforcement.

The API connects as `xivfm_app`, created by `deploy/postgres/app-role.sql`, which can read and change rows but cannot change the schema, roles, or other databases. Migrations use the database owner as a separate step. Each installation records when it was last used, updated at most once every six hours.

A background job runs every six hours and removes link sessions one day after expiry, installations 30 days after revocation, Relay invitations 30 days after they are accepted, revoked, or expire, and Relays 60 days after deletion (or longer when the creation-limit window is longer). It never removes accounts, active installations, active Relays, memberships, or removal restrictions of active Relays.

Redis stores installation heartbeat/presence records with server-controlled TTLs, account-to-installation publication pointers, Public and revision-keyed Relay/location snapshots, normalized listening cache entries, expiring per-account poll leases, and the distributed Last.fm request budget. Durable membership remains PostgreSQL-authoritative and Redis authorization data is never trusted without revision validation. Listening history is not retained.

Provider artwork is disabled by default because the currently reviewed Last.fm terms expressly exclude images/artwork. The texture-capable card and bounded plugin loader are implemented without an embedded or generated fallback cover. A deliberate `XIVFM_LASTFM_ARTWORK_ENABLED=true` controlled-development opt-in maps the largest safe HTTPS image returned by Last.fm so the full card can be tested privately or with invited external testers at the product owner's direction; broader public rollout remains blocked unless permission is recorded. The plugin attributes Last.fm in its Account UI and exposes the provider track/profile link through `/xivfm lastfm` without adding unapproved copy to the card source design. See [`lastfm-compliance.md`](lastfm-compliance.md) for the review and public-use approval gate.

## Security and privacy

- Authenticate every sync, Relay, and mutation operation.
- Do not expose an arbitrary-username Last.fm proxy.
- Never trust client-supplied track metadata.
- Validate bounded request schemas and return stable machine-readable errors.
- Hash installation and invitation credentials at rest.
- Enforce per-account, per-IP, route, and global quotas. Forwarded client addresses are accepted only from networks listed in `XIVFM_TRUSTED_PROXY_NETWORKS`; without it the connection address is used.
- Return no global player directory.
- Keep metrics free of account, character, Relay, and track labels.
- Keep detailed metrics and administration private.
- Account deletion removes the account, all installations and link sessions, owned Relays, and other Relay memberships in one transaction, then active presence and cached listening state. Pending provider polls may keep cached listening state for at most its 15-minute lifetime.
- Relay deletion is an explicit, testable operation.

## Deployment

The controlled-test deployment runs the non-root ARM64 API with persistent PostgreSQL and ephemeral Redis. PostgreSQL and Redis stay on an internal network; only the API joins an unexposed egress network for Last.fm HTTPS and the shared proxy network under a fixed alias. No XIV.fm container publishes a host port. Nginx provides conventional public HTTPS at `https://api.xivfm.form-a.page` with a renewable Let's Encrypt certificate, while the former IP-derived Nginx hostname and Tailscale Funnel remain temporary client-migration routes and unrelated Tailscale Serve handlers remain tailnet-only. Last.fm secrets remain in the mode-0600 live environment outside Git. A future public deployment still requires backups, monitoring, provider approval, and separate rollout approval.
