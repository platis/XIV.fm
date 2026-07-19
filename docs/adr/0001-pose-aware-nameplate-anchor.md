# ADR 0001: Use the game-computed nameplate world position

_Status: accepted_

_Date: 2026-07-19_

## Context

XIV.fm previously projected every card from the character's base position plus a fixed 2.45-yalm vertical offset. That approximation does not follow the animated pose. Sitting, kneeling, lying, mounted, and other timelines can therefore leave the card well above the character's current head position.

Dalamud's stable `IPlayerCharacter` interface exposes the character base position but not its animated head position or current nameplate world position. Reading skeleton bones would require substantially more model-specific memory traversal and would make race, equipment, transformation, and animation handling XIV.fm's responsibility. Reading NamePlate addon nodes would couple anchoring to UI layout and hidden-nameplate settings.

FFXIVClientStructs exposes the game's read-only `GameObject.GetNamePlateWorldPosition` function. The game already computes this point from its current model, animation, mount, and nameplate offsets.

## Decision

Use `GameObject.GetNamePlateWorldPosition` as the canonical world anchor for local and remote player cards, then project that point through Dalamud's `IGameGui.WorldToScreen` API.

The adapter will:

- Obtain the generated `Character*` only by casting the non-zero `Address` of a currently valid typed `IPlayerCharacter`; no offset arithmetic is performed.
- Call only the generated, typed `GetNamePlateWorldPosition` function.
- Validate that the returned point is finite, above the character base, and within a generous 50-yalm sanity bound.
- Skip that card for the frame if the object, pointer, result, validation, or screen projection is unavailable.
- Keep the unsafe operation isolated in one adapter and keep validation behavior in `XIV.fm.Plugin.Core`.

The fixed-height fallback is removed so an invalid memory-derived result cannot place a card at a misleading position.

## Safety review

Approved scope is a read-only pose-aware anchor lookup during Dalamud's UI draw callback.

- No game state is written.
- No hooks, custom signatures, hard-coded offsets, or arbitrary pointer arithmetic are introduced.
- The generated API is versioned with the pinned Dalamud/FFXIVClientStructs development distribution.
- The object is sourced from the current typed object table and checked with `IsValid()` immediately before access.
- Native pointers are never retained across frames.
- Returned coordinates are copied into managed `System.Numerics.Vector3` values immediately.
- Failure is local to one card and fails closed for that frame.
- Dalamud API upgrades require a successful Release build and renewed in-game checks for standing, sitting, ground-sitting, lying, mounted, transformed, and disappearing objects.

The remaining inherent risk is that any generated native call can become invalid after a game/API mismatch. Pinning the SDK, using the matching Dalamud distribution, and refusing manual offsets minimizes that risk.

## Consequences

Cards follow the same pose-aware world point used by the game rather than an estimated standing height. This introduces a narrowly scoped unsafe dependency on FFXIVClientStructs, so the plugin project explicitly enables that SDK reference and unsafe compilation. Final pixel spacing relative to the nameplate remains a Phase 6 visual-design decision.
