# XIV.fm

XIV.fm is a new Dalamud plugin and companion service that displays Last.fm listening presence on cards anchored above player nameplates in Final Fantasy XIV.

This repository is a greenfield implementation. Visual design is intentionally represented by a placeholder card while account linking, bounded Last.fm polling, map presence, privacy, and Custom Relays are built and verified.

## Current status

The current development slice renders placeholder cards above player characters using typed Dalamud APIs and world-to-screen projection. The local card and developer-only remote mock cards share one renderer driven by immutable snapshots.

Implemented:

- Exact `XIV.fm` product, assembly, manifest, and C# namespace naming.
- Dalamud API 15 plugin scaffold.
- Dalamud-independent plugin core project.
- Atomic immutable overlay-state snapshots.
- One local/remote player-card rendering path.
- Strict character name and home-world matching.
- Typed location snapshots using current world, territory, map, and instance IDs.
- Immediate snapshot invalidation/wake-up for login, logout, and location changes.
- Client-side remote distance filtering, defaulting to 8 yalms and clamped to 1–20.
- `/xivfm status` diagnostics for matching, range, projection, rendering, and location.
- Unit-tested anchoring, identity, snapshot, and visibility behavior.

Not yet implemented:

- Last.fm account linking or polling.
- Public map presence.
- Private mode behavior beyond the planned contract.
- Custom Relay creation, invitations, membership, or kicking.
- Final card and settings design.

## Documentation

- [`docs/product.md`](docs/product.md) — product scope and terminology.
- [`docs/architecture.md`](docs/architecture.md) — target plugin/server architecture.
- [`docs/plan.md`](docs/plan.md) — phased delivery plan and acceptance criteria.
- [`docs/relays.md`](docs/relays.md) — Custom Relay API, authorization, and limits.
- [`docs/releasing.md`](docs/releasing.md) — release checklist.

## Repository layout

```text
src/XIV.fm.Plugin/             Dalamud adapter and placeholder UI
src/XIV.fm.Plugin.Core/        Dalamud-independent plugin behavior
tests/XIV.fm.Plugin.Core.Tests Core unit tests
docs/                          Product, architecture, API, and delivery decisions
```

Server projects and versioned contracts will be introduced when the local-overlay foundation is accepted.

## Development controls

```text
/xivfm              Toggle all placeholder cards
/xivfm mock         Toggle mock cards on loaded remote players
/xivfm range <1-20> Set the remote render distance in yalms
/xivfm status       Print the active development settings
```

Remote mock state is disabled by default and exists only to validate matching, distance, and nameplate placement before server development.

## Toolchain

- .NET SDK 10.0.301
- Dalamud.NET.Sdk 15.0.0
- Dalamud API 15 development distribution

On Windows with XIVLauncher, `Dalamud.NET.Sdk` discovers the development files automatically. On Linux, set `DALAMUD_HOME` to an extracted current distribution:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DALAMUD_HOME=/srv/cache/dalamud/api15
```

The distribution can be obtained from:

```text
https://goatcorp.github.io/dalamud-distrib/latest.zip
```

Successful CI runs publish an `XIV.fm-development-plugin` artifact containing `latest.zip` for in-game development testing. It is not a production release.

## Quality gates

Run all formatting, tests, and builds:

```bash
./scripts/check.sh
```

Equivalent commands:

```bash
dotnet restore XIV.fm.slnx --locked-mode
dotnet format XIV.fm.slnx --verify-no-changes --no-restore
dotnet test tests/XIV.fm.Plugin.Core.Tests/XIV.fm.Plugin.Core.Tests.csproj --no-restore -c Release
dotnet build src/XIV.fm.Plugin/XIV.fm.Plugin.csproj --no-restore -c Release
```

## Privacy and secrets

Never place the Last.fm API secret in the plugin, configuration, manifests, logs, or repository. Social publication will be explicit and will support only:

- **Private** — visible only to the local user.
- **Public** — shared through a location-scoped snapshot.
- **Custom Relays** — shared with explicitly joined, invitation-based groups.

## Deployment

The future backend will run behind the shared Nginx proxy without publishing a host port. Sanitized deployment definitions belong in the infrastructure repository; credentials and persistent data remain outside Git. No backend is deployed yet.
