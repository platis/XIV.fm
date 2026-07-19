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
- Browser and texture integration.
- Typed server client.

Rendering reads an immutable snapshot. It does not call the server, Last.fm, storage, or asynchronous loaders directly.

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

The first server vertical slice authenticates a hashed opaque installation credential, validates and stores a heartbeat, and returns unavailable own-listening state plus an empty versioned snapshot. It performs no Last.fm or social-presence lookup. Custom visibility fails closed until Relay membership authorization exists. Application ports have both in-memory test adapters and durable PostgreSQL credential/Redis heartbeat adapters, without moving policy into HTTP or storage code.

The API composition root provides bounded JSON input, stable problem responses, server-controlled request IDs, per-installation sync rate limiting, liveness/readiness checks, and label-free `System.Diagnostics.Metrics` counters. Metrics have no public HTTP endpoint; a private collector/exporter will be configured during deployment work.

## Last.fm scheduling

Only the server polls Last.fm. One normalized linked account has one logical polling stream regardless of installations, maps, viewers, or Relays.

Initial policy to validate with Last.fm and load tests:

- Active and playing: target every 30 seconds.
- Active with no current track: target every 90 seconds.
- Offline: no polling after heartbeat expiry.
- Errors: bounded exponential backoff with full jitter.
- Global planning budget: 3.5 requests/second until the actual allowance is confirmed.

All requests acquire a global token. Concurrent work for one account is single-flight. Server HTTP requests return cached state and may enqueue work; they never bypass the scheduler. During upstream failure, cached state is returned with age and stale status.

## Location snapshots

A `LocationScope` is derived from stable Dalamud-provided identifiers:

```text
currentWorldId + territoryId + instanceOrWardIdWhenAvailable
```

Each authenticated client publishes only its own location/presence. The server builds reusable snapshots:

```text
public:{locationScope}
relay:{relayId}:{locationScope}
```

Public snapshots may use ETags and a short output cache. Relay snapshots always require current membership authorization. A membership change invalidates affected snapshots.

The plugin matches snapshot entries against loaded player characters and applies the default 8-yalm distance limit locally. Coordinates are not uploaded.

## Account linking

The plugin is a public client and contains no Last.fm secret.

1. Plugin requests a short-lived device/link session.
2. Browser completes Last.fm authorization.
3. Server validates one-time state and exchanges the provider token.
4. Server records the canonical Last.fm account.
5. Plugin receives an opaque, revocable XIV.fm installation credential.

`user.getRecentTracks` does not require the Last.fm session key. The server discards that provider session after ownership proof unless an approved future write feature requires retention.

## Persistence

PostgreSQL currently stores installation IDs and unique SHA-256 hashes of high-entropy credentials, including rotation/revocation timestamps, through checked-in EF Core migrations. Later migrations add account links, link sessions, Custom Relays, memberships, hashed invitations, ownership, removal restrictions, and quota events.

Redis currently stores installation heartbeat/presence records with server-controlled TTLs. Later adapters add normalized track cache, snapshot material, poll leases, single-flight locks, and distributed rate counters. Listening history is not retained in the initial architecture.

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

The server will run as a non-root ARM64-compatible container behind Nginx on the shared Docker `proxy` network. PostgreSQL, Redis, metrics, and administration remain on private networks with no host port publication. Deployment and ingress require a separate approved infrastructure change.
