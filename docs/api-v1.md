# XIV.fm v1 sync contract

_Status: sync frozen; account-link and Custom Relay operations are additive_

## Purpose

`POST /v1/sync` is the plugin's single recurring server operation. One request:

1. Authenticates an opaque XIV.fm installation credential.
2. Refreshes the caller's heartbeat, character identity, location, and selected visibility.
3. Returns the caller's cached listening state.
4. Returns a reusable, authorized location-presence snapshot or confirms that the client's known snapshot is current.

The handler returns cached state and may enqueue bounded background work. It never performs an unbudgeted Last.fm lookup inline.

The machine-readable OpenAPI 3.1 document is [`openapi/v1.openapi.json`](openapi/v1.openapi.json).

For linked installations, sync returns the latest normalized cached Last.fm observation and enqueues account activity for the background polling coordinator; the handler never performs a listening lookup inline. Unlinked development installations return `unavailable` and may sync only in Private mode. Public sync requires a linked account and returns a shared, bounded 20-second snapshot for the exact world/territory/map/instance scope. Custom sync requires current membership in every selected Relay and returns the authorized union of shared per-Relay/location snapshots.

## Account linking

Initial installation credentials are created only after Last.fm account proof:

1. `POST /v1/account-links` creates a ten-minute session and returns a Last.fm `authorizationUrl`, a session ID, and a high-entropy `linkCredential` exactly once.
2. The plugin stores that secret locally, opens the authorization URL, and polls `POST /v1/account-links/{linkSessionId}/status` with the secret in a bounded JSON body.
3. Last.fm redirects the browser to `GET /v1/account-links/{linkSessionId}/callback` with the one-time state and provider request token. The server atomically claims both values before exchanging the provider token.
4. Successful proof records the canonical Last.fm identity and promotes the existing link-credential hash to a new installation credential in the same logical completion operation. The plaintext secret is not returned again.
5. The Last.fm session key is checked as proof and immediately discarded. XIV.fm remains read-only and later uses `user.getRecentTracks`, which does not require that key.

Callback state, provider tokens, and link/installation credentials are independently hashed at rest. A callback is invalid after expiry or its first claim, so replay does not repeat provider exchange or credential issuance. Status lookup deliberately returns the same `404 account_link_not_found` response for an unknown session and a wrong link credential.

Account-link routes are the only unauthenticated non-health operations. They have per-IP route limits, no-store responses, bounded ten-second Last.fm calls, and share the initial 3.5-request/second Last.fm budget. Durable replicas coordinate that budget through Redis. They do not provision a credential without provider proof.

## Custom Relays

All Relay operations require an authenticated installation associated with a linked account. The v1 resource surface supports Relay create/list/read/rename/delete, owner-only member and invitation management, member leave, and secret-bearing invitation preview/accept. The complete routes and schemas are frozen additively in the OpenAPI document.

Relay creation uses a client UUID idempotency key. Names are normalized, trimmed, bounded to 3–48 Unicode scalar values, and reject control characters. Durable account quotas cover active ownership, one-minute bursts, and a retained rolling 30-day creation window, so deletion cannot reset quota history.

Invitation tokens contain 256 bits of entropy, are returned once, and are stored only as SHA-256 hashes. They expire after at most seven days and are atomically single-use. An account kicked by the owner cannot use an invitation issued before the kick; a newly issued owner invitation explicitly clears that restriction when accepted.

Membership mutations increment a durable Relay revision. Custom snapshot reads establish current membership and revision before reading shared cache material, then revalidate both before responding. A kick or leave also strips that Relay from the account's active publications and invalidates cached Relay snapshots. These checks make stale client selection and stale cache entries fail closed.

## Transport and authentication

```http
POST /v1/sync HTTP/1.1
Authorization: Bearer <opaque-installation-credential>
Content-Type: application/json
```

Credentials are XIV.fm installation credentials, not Last.fm keys or sessions. The server stores only a credential hash. TLS is mandatory outside local development.

Successful and error responses include `X-Request-ID`. The server controls request IDs and may replace invalid caller-provided values.

Initial credential provisioning is internal to successful account-link completion; there is no endpoint that creates an authenticating installation without provider proof. Authenticated installations can manage the current credential:

```http
POST   /v1/installations/current/credential
DELETE /v1/installations/current
```

Rotation invalidates the authenticating credential atomically and returns its replacement exactly once. Revocation makes the current credential unusable. Both operations are rate-limited and return `Cache-Control: no-store`.

## Duty invariant

A duty-bound client does not call `/v1/sync` or any other XIV.fm endpoint. On duty entry it cancels in-flight work where possible, clears cards, and sends no final leave request. Existing presence expires through its short server TTL. Duty state is therefore not present in any request schema and is not trusted as server input.

## Request

```json
{
  "pluginVersion": "0.1.2.0",
  "character": {
    "name": "Alice Cat",
    "homeWorldId": 54
  },
  "location": {
    "currentWorldId": 63,
    "territoryId": 129,
    "mapId": 130,
    "instanceId": 2
  },
  "visibility": {
    "mode": "custom",
    "relayIds": ["5be0d1e2-0a63-4f16-ad4d-d53e95b7c97f"]
  },
  "knownSnapshotVersion": "opaque-snapshot-version"
}
```

### Invariants

- Character matching uses trimmed name plus non-zero home-world ID.
- Current world, territory, and map IDs are required and non-zero. Instance ID is zero only when Dalamud has no instance/ward value.
- Coordinates, object IDs, party data, duty state, and client-authored track metadata are never sent.
- `private` and `public` require an empty `relayIds` array.
- `public` requires a successfully linked account; unlinked development credentials receive `403 linked_account_required`.
- `custom` requires one to five distinct currently joined Relay IDs and a linked account.
- The server validates Relay membership and membership revisions on every custom sync before and after shared snapshot reads.
- `knownSnapshotVersion` is an opaque value copied from the preceding response. Clients do not interpret or construct it.

The sync itself is the heartbeat. A client does not need a separate recurring heartbeat endpoint.

## Response

```json
{
  "serverTime": "2026-07-19T08:00:00Z",
  "presenceExpiresAt": "2026-07-19T08:01:00Z",
  "nextSyncAfterSeconds": 30,
  "ownListening": {
    "status": "playing",
    "isStale": false,
    "observedAt": "2026-07-19T07:59:52Z",
    "track": {
      "title": "Example Track",
      "artist": "Example Artist",
      "album": "Example Album",
      "albumArtUrl": "https://lastfm.freetls.fastly.net/example.jpg",
      "trackUrl": "https://www.last.fm/music/example",
      "startedAt": null
    }
  },
  "locationPresence": {
    "version": "opaque-snapshot-version-2",
    "snapshot": {
      "location": {
        "currentWorldId": 63,
        "territoryId": 129,
        "mapId": 130,
        "instanceId": 2
      },
      "generatedAt": "2026-07-19T07:59:59Z",
      "expiresAt": "2026-07-19T08:00:20Z",
      "entries": []
    }
  }
}
```

`nextSyncAfterSeconds` is server guidance and must be clamped by the plugin's safe timing policy. It is not permission to bypass local backoff or the duty gate.

Listening status is one of:

- `playing` — `track` is present.
- `notPlaying` — no current track; `track` is null.
- `unavailable` — no usable provider/cache result; `track` is null.

The `albumArtUrl` field remains nullable for compatibility, but the current Last.fm adapter always returns null because the reviewed provider terms exclude artwork. It must not be populated without an updated compliance review and provider permission.

`isStale` explicitly describes cached freshness. Playing observations become stale after 60 seconds and not-playing observations after 180 seconds. During provider errors the last cache remains available and its age continues increasing; a missing or expired cache returns `unavailable`. `observedAt` is null only when no upstream observation exists.

For Private visibility, the server returns an empty location snapshot. Public and Custom responses contain only the authorized union for the exact location scope. The plugin still matches loaded character identities and applies distance filtering locally.

If `knownSnapshotVersion` is still current, `locationPresence.version` is returned and `locationPresence.snapshot` is null. This avoids retransmitting a shared snapshot while still returning fresh heartbeat and own-listening metadata. A future conditional GET may expose the same version through an HTTP ETag; snapshot versions remain opaque in either transport.

## Presence lifecycle

- Every accepted sync extends `presenceExpiresAt`.
- Private sync removes any prior social publication while retaining the authenticated installation heartbeat needed for own listening state.
- Public/Custom publication expires automatically when sync stops.
- Logout, process termination, network loss, and duty entry are safe because expiration does not require a final request.
- The server stops treating an account as active, and therefore stops Last.fm polling, after all relevant heartbeats expire.

## Errors

Errors use an RFC 9457-style body plus stable `code` and `requestId` fields:

```json
{
  "type": "https://xiv.fm/problems/relay-membership-required",
  "title": "Relay membership required",
  "status": 403,
  "code": "relay_membership_required",
  "requestId": "01J3EXAMPLE",
  "detail": "The installation is not a current member of one selected Relay.",
  "instance": "/v1/sync"
}
```

Expected status classes:

- `400` malformed or invalid bounded input.
- `401` missing, invalid, expired, or revoked installation credential.
- `403` linked-account or Relay authorization failure.
- `409` installation/account state conflict.
- `429` route/account/IP quota exceeded, with `Retry-After` where meaningful.
- `503` required server dependency unavailable; cached listening data should be preferred over this response when possible.

Clients branch on `code`, not human-readable `title` or `detail`.

## Compatibility

V1 changes are additive after the first server-backed development build. Existing fields and enum wire values are not renamed or repurposed. A breaking change requires `/v2` and a migration window. Unknown response properties must be ignored; unknown enum values fail closed and trigger a client update requirement rather than changing privacy behavior.
