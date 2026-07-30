# XIV.fm

XIV.fm is a new Dalamud plugin and companion service that displays Last.fm listening presence on cards anchored above player nameplates in Final Fantasy XIV.

This repository is a greenfield implementation. The approved card and settings designs are implemented and have completed representative in-game visual validation.

## Current status

The current development slice links Last.fm through the browser, synchronizes cached listening state, publishes Public or membership-authorized Custom Relay presence, and renders listening state above matched characters using typed Dalamud APIs and world-to-screen projection. Local, remote, and developer-only mock cards share one renderer driven by immutable snapshots.

Implemented:

- Exact `XIV.fm` product, assembly, manifest, and C# namespace naming.
- Dalamud API 15 plugin scaffold.
- Dalamud-independent plugin core project.
- Atomic immutable overlay-state snapshots.
- One local/remote player-card rendering path.
- Pose-aware anchoring from the game's current nameplate world position, including emote and mount offsets.
- A refined compact listening card, content-sized up to 297×75.2 px, with uniform 10 px outer padding, a 10 px artwork/text gap, explicitly centered artwork, bottom-aligned text, a slow seamless title marquee and three-dot artist truncation for overflow, a configurable FFXIV-style charcoal surface defaulting to 60% opacity, independent 50–150% local/other-player sizing, smooth distance-based remote scaling with a 50% readability floor, optional cover artwork with no reserved gap when unavailable, and a 0.4-yalm nameplate safety height.
- Strict character name and home-world matching.
- Typed location snapshots using current world, territory, map, and instance IDs.
- Immediate snapshot invalidation/wake-up for login, logout, and location changes.
- Duty participation gating that hides cards and blocks future server requests while bound by duty.
- Versioned v1 sync transport contracts and an OpenAPI 3.1 document.
- ASP.NET Core modular monolith with authenticated sync, bounded validation, structured errors, request IDs, health checks, rate limits, and metrics instrumentation.
- PostgreSQL credential persistence/migrations, Redis heartbeat TTLs, credential lifecycle endpoints, and a disposable loopback-only container integration stack.
- A typed plugin network client and cancellable duty-aware development sync coordinator.
- Ten-minute replay-protected Last.fm browser-link sessions, server-side ownership proof, canonical account persistence, and proof-gated installation credentials.
- Normalized cached Last.fm listening state with Redis single-flight leases, a distributed 3.5-request/second budget, latency-oriented adaptive 15/20-second polling, jitter, backoff, circuit breaking, and explicit stale metadata through five-second sync.
- Duty-gated plugin browser linking with automatic default-browser launch, persistent manual reopen/copy fallbacks, persisted installation credentials, playing-only local cards that disappear when nothing is playing or listening state is unavailable, explicit stale metadata in sync/diagnostics, and Last.fm track/profile links.
- Private/Public selection, shared 20-second location snapshots with opaque versions and metrics, strict loaded-character matching, and server-authoritative remote listening cards.
- Custom Relay ownership, bounded membership, soft deletion, replay-safe creation, hashed single-use invitations, join/leave/kick restrictions, and durable quota enforcement.
- Membership-authorized Custom sync and shared Relay/location snapshots with revision-based immediate kick invalidation.
- A brief first-run welcome with plain-language product guidance, direct Last.fm linking, and a GitHub link, followed by a cohesive FFXIV-style charcoal settings window with live link/sync states, safe Last.fm disconnection, Overlay controls, direct privacy choices, and table-based Custom Relay details and owner controls. Runtime diagnostics and private-test server configuration stay hidden unless `/xivfm dev` is entered.
- A native `.FM` server-info-bar shortcut that uses the bar’s default yellow while cards are visible and red while hidden; left-click toggles cards and right-click opens settings.
- Independent global and own-card visibility controls, plus client-side remote distance filtering defaulting to 8 yalms and clamped to 1–20. Other players’ cards shrink smoothly beyond 2 yalms toward the configured limit.
- Hold-Alt card interaction that preserves normal click-through gameplay, highlights hovered listening cards immediately, and opens validated Last.fm HTTPS track links in the operating-system browser.
- `/xivfm status` diagnostics for matching, range, projection, rendering, and location.
- Unit-tested anchoring, identity, snapshot, and visibility behavior.

Not yet implemented:

- Broader public use of provider album covers, which remains blocked until an artwork source grants appropriate permission; controlled private or invited external testing can explicitly opt in at the product owner's direction.

## Install the development build

In Dalamud Settings, open **Experimental → Custom Plugin Repositories** and add:

```text
https://raw.githubusercontent.com/platis/XIV.fm/main/repository/pluginmaster.json
```

Save the settings, open the plugin installer, search for **XIV.fm**, and install it. Development releases are prereleases intended for in-game testing and are not an official Dalamud repository listing.

## Join the invited external test

Install XIV.fm and connect Last.fm from the Account screen. The plugin uses the invited public test endpoint by default with no URL entry or development-server toggle required:

```text
https://xivfm.168.138.129.70.sslip.io
```

Existing installations configured with the former placeholder, Funnel address, or public endpoint under Development server are migrated automatically. The endpoint is temporary and intended only for invited testing. At the product owner's direction, Last.fm artwork is enabled for this controlled test; linking, listening-state sync, Public presence, and Custom Relays are available.

## Documentation

- [`docs/product.md`](docs/product.md) — product scope and terminology.
- [`docs/api-v1.md`](docs/api-v1.md) — frozen v1 sync behavior and wire contract.
- [`docs/architecture.md`](docs/architecture.md) — target plugin/server architecture.
- [`docs/plan.md`](docs/plan.md) — phased delivery plan and acceptance criteria.
- [`docs/relays.md`](docs/relays.md) — Custom Relay API, authorization, and limits.
- [`docs/releasing.md`](docs/releasing.md) — automated prerelease process.
- [`docs/lastfm-compliance.md`](docs/lastfm-compliance.md) — reviewed provider constraints and public-use gates.
- [`docs/adr/`](docs/adr/) — approved architecture decisions and safety reviews.
- [`CHANGELOG.md`](CHANGELOG.md) — user-visible release history.

## Repository layout

```text
src/XIV.fm.Contracts/          Versioned plugin/server transport contracts
src/XIV.fm.Plugin/             Dalamud adapter and ImGui presentation
src/XIV.fm.Plugin.Core/        Dalamud-independent plugin behavior
src/XIV.fm.Plugin.Network/     Typed bounded HTTP client
src/XIV.fm.Server.*/           API, Application, Domain, and Infrastructure modules
tests/XIV.fm.Contracts.Tests   Wire-format contract tests
tests/XIV.fm.Plugin.Core.Tests Core unit tests
tests/XIV.fm.Server.Tests      Server integration and credential-lifecycle tests
docs/                          Product, OpenAPI, architecture, and delivery decisions
```

An ARM64 controlled-test backend stack is running with PostgreSQL persistence, ephemeral Redis, no published container ports, and conventional public HTTPS through Nginx at the temporary `sslip.io` hostname. Last.fm credentials are configured outside Git, artwork is explicitly enabled for the controlled test, and browser proof, linked sync, and in-game card behavior have been validated. See [`src/XIV.fm.Server.Api/README.md`](src/XIV.fm.Server.Api/README.md) for development and runtime details.

## Development controls

```text
/xivfm              Open XIV.fm settings
/xivfm link         Open settings and start duty-gated Last.fm browser authorization
/xivfm toggle       Toggle all cards
/xivfm lastfm       Open the current track or linked Last.fm profile
/xivfm visibility <private|public>
                    Select private or location-scoped publication
/xivfm mock         Toggle mock cards on loaded remote players
/xivfm range <1-20> Set the remote render distance in yalms
/xivfm status       Print account, sync, and rendering diagnostics
/xivfm dev          Reveal the Diagnostics tab for the current plugin session
```

Remote mock state is disabled by default and exists only to validate matching, distance, and nameplate placement. The Diagnostics tab—including private development-server configuration—is hidden until `/xivfm dev` is entered and is hidden again after the plugin reloads. The color-coded `.FM` shortcut in the native server info bar mirrors the Overlay setting; its tooltip reports whether cards are visible.

The normal client uses the invited public HTTPS endpoint automatically; explicit development mode additionally accepts loopback HTTP/HTTPS. Listening cards remain click-through during normal gameplay; holding Alt temporarily enables card interaction, and clicking a highlighted card opens its validated Last.fm track link. `/xivfm link` creates a short-lived connection link, attempts to open it in the operating-system default browser, and keeps **Open Last.fm authorization** and **Copy connection link** available in Account settings until linking completes. Successful proof stores the opaque installation credential in Dalamud's local plugin configuration and sync begins automatically. Linking, polling, sync, and rendering all suspend while bound by duty.

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
dotnet test tests/XIV.fm.Contracts.Tests/XIV.fm.Contracts.Tests.csproj --no-restore -c Release
dotnet test tests/XIV.fm.Plugin.Core.Tests/XIV.fm.Plugin.Core.Tests.csproj --no-restore -c Release
dotnet test tests/XIV.fm.Server.Tests/XIV.fm.Server.Tests.csproj --no-restore -c Release
dotnet build src/XIV.fm.Server.Api/XIV.fm.Server.Api.csproj --no-restore -c Release
dotnet build src/XIV.fm.Plugin/XIV.fm.Plugin.csproj --no-restore -c Release
```

## Privacy and secrets

Never place the Last.fm API secret in the plugin, configuration, manifests, logs, or repository. Social publication will be explicit and will support only:

- **Private** — visible only to the local user.
- **Public** — shared through a location-scoped snapshot.
- **Custom Relays** — shared with explicitly joined, invitation-based groups.

## Deployment

The controlled-test backend runs from `/srv/stacks/xivfm`, publishes no host ports, and joins the shared proxy network only so Nginx can reach its internal API listener. Public testing uses `https://xivfm.168.138.129.70.sslip.io`; PostgreSQL and Redis remain internal, and unrelated Tailscale Serve handlers remain private. Sanitized definitions live in the infrastructure repository, while credentials and persistent data remain outside Git. The temporary IP-derived hostname must be replaced with an owned domain before an approved public rollout.
