# XIV.fm v1 sync contract

_Status: frozen for the first authenticated plugin/server vertical slice_

## Purpose

`POST /v1/sync` is the plugin's single recurring server operation. One request:

1. Authenticates an opaque XIV.fm installation credential.
2. Refreshes the caller's heartbeat, character identity, location, and selected visibility.
3. Returns the caller's cached listening state.
4. Returns a reusable, authorized location-presence snapshot or confirms that the client's known snapshot is current.

The handler returns cached state and may enqueue bounded background work. It never performs an unbudgeted Last.fm lookup inline.

The machine-readable OpenAPI 3.1 document is [`openapi/v1.openapi.json`](openapi/v1.openapi.json).

## Transport and authentication

```http
POST /v1/sync HTTP/1.1
Authorization: Bearer <opaque-installation-credential>
Content-Type: application/json
```

Credentials are XIV.fm installation credentials, not Last.fm keys or sessions. The server stores only a credential hash. TLS is mandatory outside local development.

Successful and error responses include `X-Request-ID`. The server controls request IDs and may replace invalid caller-provided values.

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
- `custom` requires one or more currently joined Relay IDs, bounded by the server's selected-Relay limit.
- The server validates Relay membership on every custom sync.
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

`isStale` explicitly describes cached freshness. `observedAt` is null only when no upstream observation exists.

For Private visibility, the server returns an empty location snapshot. Public and Custom responses contain only the authorized union for the exact location scope. The plugin still matches loaded character identities and applies distance filtering locally.

If `knownSnapshotVersion` is still current, `locationPresence.version` is returned and `locationPresence.snapshot` is null. This avoids retransmitting a shared snapshot while still returning fresh heartbeat and own-listening metadata. Phase 4 may expose the same snapshot through a conditional GET with an HTTP ETag; snapshot versions remain opaque in either transport.

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
