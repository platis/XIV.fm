# XIV.fm server development host

This project is the ASP.NET Core composition root for the XIV.fm modular monolith. It supports:

- `memory` storage for automated tests and loopback-only plugin development.
- `durable` storage using PostgreSQL for installation credentials and Redis for expiring presence heartbeats.

No server stack is deployed by this repository.

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
- Verifies hashed credential persistence and Redis heartbeat TTL.
- Removes containers, networks, volumes, generated environment files, and the test API image on exit.

`deploy/compose.integration.yaml` is disposable test infrastructure, not a production deployment definition.

## Durable configuration

The API accepts these runtime settings:

```text
XIVFM_STORAGE_MODE=durable
ConnectionStrings__Postgres=Host=...;Database=...;Username=...;Password=...
ConnectionStrings__Redis=host:6379,abortConnect=false
XIVFM_APPLY_MIGRATIONS=false
```

Production migration execution should be a controlled deployment step. Automatic startup migration exists only for disposable integration workflows.

## Endpoints

- `GET /health/live`
- `GET /health/ready`
- `POST /v1/sync`
- `POST /v1/installations/current/credential` — rotate and return a replacement once.
- `DELETE /v1/installations/current` — revoke the current installation.

All non-health endpoints require `Authorization: Bearer <credential>`. Initial credential provisioning is an application service intended for the account-link completion flow; there is deliberately no unauthenticated provisioning endpoint.

The current slice makes no Last.fm call and returns no social presence. Custom visibility fails closed until Relay membership authorization exists.
