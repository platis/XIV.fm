# XIV.fm server development host

This project is the ASP.NET Core composition root for the XIV.fm modular monolith. The current vertical slice uses process-local credential and heartbeat stores only. It is suitable for automated tests and local contract integration, not deployment or shared use.

## Run locally

Generate a temporary high-entropy credential and keep the listener on loopback:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://127.0.0.1:5080
export XIVFM_DEV_INSTALLATION_CREDENTIAL="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"
dotnet run --project src/XIV.fm.Server.Api/XIV.fm.Server.Api.csproj
```

The development credential is accepted only when `ASPNETCORE_ENVIRONMENT=Development`. It is hashed immediately and is never logged. Do not place it in source, committed settings, shell history, or a production environment.

Endpoints:

- `GET /health/live`
- `GET /health/ready`
- `POST /v1/sync` with `Authorization: Bearer <credential>`

No Last.fm call, social snapshot read, database, Redis operation, or external network dependency occurs in this slice. Custom visibility fails closed until Relay membership authorization exists.
