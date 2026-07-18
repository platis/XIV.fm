# XIV.fm Custom Relays

## Model

A Custom Relay is an arbitrarily named, invitation-based audience. There are no Relay types and no role hierarchy.

```text
Relay
  id
  name
  ownerAccountId
  createdAt
  updatedAt
  deletedAt

RelayMembership
  id
  relayId
  accountId
  joinedAt

RelayInvitation
  id
  relayId
  tokenHash
  createdAt
  expiresAt
  acceptedAt
  revokedAt

RelayRemoval
  relayId
  accountId
  removedAt
```

The owner is a resource owner, not a general role. Ownership is required so rename, invitation, kick, transfer, and deletion decisions have one authority.

## Authorization

Owner operations:

- Rename and delete the Relay.
- List members.
- Create and revoke invitations.
- Kick a member.
- Initiate ownership transfer when that feature is implemented.

Member operations:

- Read authorized Relay presence.
- Select the Relay as a publication audience.
- Leave, unless they are the current owner.

The server checks authorization on every operation. The client never decides that membership is valid.

## Proposed API

All endpoints are under `/v1` and require an authenticated linked account unless explicitly documented otherwise.

```http
POST   /relays
GET    /relays
GET    /relays/{relayId}
PATCH  /relays/{relayId}
DELETE /relays/{relayId}

GET    /relays/{relayId}/members
DELETE /relays/{relayId}/members/{membershipId}
DELETE /relays/{relayId}/membership

POST   /relays/{relayId}/invitations
GET    /relays/{relayId}/invitations
DELETE /relays/{relayId}/invitations/{invitationId}
POST   /relay-invitations/preview
POST   /relay-invitations/accept
```

Ownership transfer, if needed, uses a two-party proposal/acceptance flow. The owner cannot leave or kick themselves before transferring or deleting the Relay.

### Create

```json
{
  "name": "Late Night Listeners",
  "idempotencyKey": "client-generated-uuid"
}
```

Names are Unicode-normalized, trimmed, bounded, and reject control characters. Names are not globally unique; opaque IDs identify Relays.

### Invite

Invitation tokens are high-entropy, stored only as hashes, single-use by default, expiring, and revocable. Plaintext is returned once. Sensitive invitation values must be redacted from proxy/application logs.

### Kick

```http
DELETE /v1/relays/{relayId}/members/{membershipId}
```

In one logical operation the server:

1. Verifies ownership and that the target is not the owner.
2. Removes membership transactionally.
3. Records removal so an old invitation cannot immediately restore access.
4. Removes the account's presence from that Relay.
5. Invalidates affected Relay/location snapshots and authorization caches.
6. Emits a non-personal audit event.

A new explicit owner invitation can allow the account to rejoin and clear the prior removal restriction.

## Presence integration

The plugin selects exactly one visibility mode:

```json
{ "mode": "private", "relayIds": [] }
{ "mode": "public", "relayIds": [] }
{ "mode": "custom", "relayIds": ["relay-id-1", "relay-id-2"] }
```

The server verifies current membership for every selected Relay. Track data remains server-authoritative and is polled once per Last.fm account. Relays add cached audience reads, not additional Last.fm calls.

Relay presence snapshots are keyed by Relay and location. A sync response may return the authorized union while internally reusing per-Relay snapshots.

## Initial configurable limits

| Limit | Default |
|---|---:|
| Active Relays owned per account | 3 |
| Relay creations per rolling 30 days | 10 |
| Relays joined per account | 20 |
| Relays selected for publication | 5 |
| Members per Relay | 100 |
| Active invitations per Relay | 20 |
| Invitation lifetime | 7 days |
| Uses per invitation | 1 |
| Relay name length | 3–48 characters |

The active ownership limit alone is insufficient because repeated create/delete operations could bypass it. Creation also has a one-per-minute burst limit, a rolling account limit, an IP limit, idempotency enforcement, and retained deletion/quota events.

Limits are server configuration and stable error contracts, not scattered constants. Limit responses use `429` or a conflict response as appropriate, a machine-readable code such as `relay_creation_limit_reached`, and `Retry-After` when meaningful.

## Concurrency and consistency

- PostgreSQL unique constraints prevent duplicate membership.
- Creation and invitation acceptance are idempotent.
- Ownership, kick, leave, and acceptance use transactions.
- Redis caches are invalidated after the durable transaction commits.
- Authorization fails closed if membership cannot be established.
- Presence TTL remains a final cleanup mechanism after disconnects or partial failures.
