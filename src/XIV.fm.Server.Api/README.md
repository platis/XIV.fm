# XIV.fm server development host

This project is the ASP.NET Core composition root for the XIV.fm modular monolith. It supports:

- `memory` storage for automated tests and loopback-only plugin development.
- `durable` storage using PostgreSQL for linked Last.fm accounts, account-link sessions, installation credentials, and Custom Relay state, plus Redis for expiring presence and shared snapshots.

The source repository does not deploy infrastructure itself. A sanitized private runtime definition lives in `/srv/projects/infrastructure/stacks/xivfm`; its live mode-0600 environment and persistent PostgreSQL data remain outside Git.

## Run with in-memory adapters

Generate a temporary high-entropy credential and keep the listener on loopback:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://127.0.0.1:5080
export XIVFM_STORAGE_MODE=memory
export XIVFM_DEV_INSTALLATION_CREDENTIAL="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"
dotnet run --project src/XIV.fm.Server.Api/XIV.fm.Server.Api.csproj
```

The development credential is accepted only when `ASPNETCORE_ENVIRONMENT=Development`. It is hashed immediately and is never logged. Do not place plaintext credentials in source or committed settings.

## Disposable durable integration stack

Run the pinned ARM64-compatible API/PostgreSQL/Redis stack and its authenticated smoke test:

```bash
./scripts/test-containers.sh
```

The script:

- Generates temporary credentials.
- Publishes only the API and only on `127.0.0.1:15080`.
- Publishes no PostgreSQL or Redis host port.
- Applies the checked-in EF Core migration.
- Verifies hashed credential and Relay-invitation persistence, Redis heartbeat TTL, linked Public and Custom Relay snapshot material/TTL, and immediate Private publication removal.
- Removes containers, networks, volumes, generated environment files, and the test API image on exit.

`deploy/compose.integration.yaml` is disposable test infrastructure, not a production deployment definition.

## Private development deployment

The current private stack publishes no container or database ports. Kestrel listens on `/srv/data/xivfm/run/api.sock`, exposed at `https://oracle-dev.tail9c4140.ts.net` through private Tailscale Serve only. Tailscale Funnel remains disabled. PostgreSQL and Redis use an internal network; only the API has unexposed outbound access for Last.fm HTTPS. Initial migrations were applied as a controlled one-time startup and automatic migration is disabled for normal restarts.

## Durable configuration

The API accepts these runtime settings:

```text
XIVFM_STORAGE_MODE=durable
ConnectionStrings__Postgres=Host=...;Database=...;Username=...;Password=...
ConnectionStrings__Redis=host:6379,abortConnect=false
XIVFM_APPLY_MIGRATIONS=false
XIVFM_PUBLIC_BASE_URL=https://api.example.invalid
XIVFM_LASTFM_API_KEY=<server-side Last.fm API key>
XIVFM_LASTFM_SHARED_SECRET=<server-side Last.fm shared secret>
XIVFM_RELAYS__MAXIMUMACTIVEOWNEDRELAYS=3
XIVFM_RELAYS__MAXIMUMCREATIONSPERROLLINGWINDOW=10
XIVFM_RELAYS__CREATIONROLLINGWINDOWDAYS=30
XIVFM_RELAYS__CREATIONBURSTWINDOWSECONDS=60
XIVFM_RELAYS__MAXIMUMJOINEDRELAYS=20
XIVFM_RELAYS__MAXIMUMMEMBERSPERRELAY=100
XIVFM_RELAYS__MAXIMUMACTIVEINVITATIONSPERRELAY=20
XIVFM_RELAYS__INVITATIONLIFETIMEHOURS=168
XIVFM_RELAYS__MAXIMUMINVITATIONLIFETIMEHOURS=168
XIVFM_RELAYS__MAXIMUMSELECTEDRELAYS=5
```

`XIVFM_PUBLIC_BASE_URL` is the externally reachable HTTPS origin used for Last.fm callbacks. The API key is safe to send to Last.fm and appears in provider authorization URLs; the shared secret remains server-side and must never enter plugin configuration, logs, images, or Git.

Production migration execution should be a controlled deployment step. Automatic startup migration exists only for disposable integration workflows.

## Endpoints

- `GET /health/live`
- `GET /health/ready`
- `POST /v1/account-links` — start a short-lived Last.fm browser-link session.
- `POST /v1/account-links/{linkSessionId}/status` — poll using the secret link credential.
- `GET /v1/account-links/{linkSessionId}/callback` — one-time Last.fm browser callback.
- `POST /v1/sync`
- `POST /v1/installations/current/credential` — rotate and return a replacement once.
- `DELETE /v1/installations/current` — revoke the current installation.
- `/v1/relays` and `/v1/relays/{relayId}` — create, list, read, rename, and delete Relays.
- `/v1/relays/{relayId}/members...` — owner member listing/kick and member leave.
- `/v1/relays/{relayId}/invitations...` — owner invitation creation/list/revocation.
- `/v1/relay-invitations/preview` and `/accept` — secret-bearing invitation operations.

Account-link start, status, and callback are proof-gated, rate-limited public operations. Other non-health endpoints require `Authorization: Bearer <credential>`. Successful Last.fm proof promotes the link credential to an installation credential; there is deliberately no unauthenticated endpoint that provisions one without proof.

The current slice calls Last.fm for explicit account authorization and through the background listening coordinator. Sync handlers only enqueue account activity and return normalized cached listening state with freshness metadata. Durable replicas share Redis poll leases and a global 3.5-request/second token bucket. Label-free metrics cover cache/poll/lease behavior. Provider artwork is intentionally ignored under the reviewed Last.fm terms; see [`docs/lastfm-compliance.md`](../../docs/lastfm-compliance.md). Linked Public installations receive shared, bounded Redis location snapshots with opaque versions; Private removes publication and returns an empty snapshot. Custom visibility requires current membership in every selected Relay and returns a revision-authorized union of shared Relay/location snapshots.
