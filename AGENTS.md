# XIV.fm Agent Instructions

Read `README.md` and every file under `docs/` before project-wide changes. Keep documentation and commands current as architecture decisions change.

## Product invariants

- User-facing branding, the plugin assembly/internal name, and C# root namespace are `XIV.fm`.
- The visibility model contains only Private, Public, and Custom Relays.
- The default remote-card render distance is 8 yalms and distance filtering happens in the plugin.
- Public presence is scoped by game location and served from shared, time-bounded snapshots.
- Custom Relays have one owner and members; there is no role hierarchy.
- Duty-bound clients render no cards and initiate no XIV.fm server requests.
- The recurring v1 operation is authenticated `/v1/sync`; its request contains no duty state, coordinates, object IDs, or client-authored track metadata.
- In-memory server adapters and development credentials are local-test scaffolding and must never be treated as production persistence or authentication provisioning.
- Visual design remains deliberately placeholder-quality until core behavior is complete, but cards must remain anchored above player characters/nameplates.
- Last.fm calls must be cached, single-flight, adaptively scheduled, and globally rate-limited. HTTP handlers may not initiate unbounded upstream work.

## Engineering rules

- Keep domain/application behavior independent from Dalamud, ImGui, ASP.NET Core, storage, and Last.fm adapters.
- UI rendering consumes immutable state and never performs network operations.
- Prefer typed Dalamud APIs; do not introduce game-memory access without an approved ADR and safety review.
- Treat API contracts, privacy, authorization, quotas, migrations, and observability as product behavior.
- Add or update tests with behavior changes.
- Never commit credentials, private keys, production `.env` files, persistent runtime data, or downloaded Dalamud distributions.
- Preserve user changes and inspect `git status` before editing.
- Do not publish, deploy, create ingress, or mutate infrastructure without explicit approval.
- For deployment work, first read `/srv/projects/infrastructure/PLAN.md` and its `AGENTS.md`.

## Commands

```bash
export PATH="$HOME/.dotnet:$PATH"
export DALAMUD_HOME=/srv/cache/dalamud/api15
./scripts/check.sh
```

The check script verifies formatting, lockfiles, unit tests, and a Release plugin build. Run it before finishing a change.
