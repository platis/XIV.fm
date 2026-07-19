# Changelog

All notable XIV.fm changes are documented here. Development builds remain prereleases until the plugin and server reach their stable acceptance criteria.

## Unreleased

### Added

- Hide all cards while bound by duty and expose the same policy for future server-request gating.
- Report duty suspension in `/xivfm status` diagnostics.

## [0.1.1] - 2026-07-19

### Fixed

- Defer initial object-table access to Dalamud's framework thread so the plugin can load safely.

## [0.1.0] - 2026-07-18

### Added

- Greenfield .NET 10 and Dalamud API 15 project foundation.
- Placeholder card projected above the local player.
- Developer mock cards for loaded remote players.
- Immutable overlay snapshots and one local/remote rendering path.
- Strict character name and home-world matching.
- Client-side remote distance filtering with an 8-yalm default.
- Typed current-world, territory, map, and instance snapshots.
- Login, logout, location-change, projection, and rendering diagnostics.
- Public GitHub-backed Dalamud custom repository tooling.
