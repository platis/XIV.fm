# XIV.fm product definition

## Vision

XIV.fm lets Final Fantasy XIV players express what they are listening to through restrained cards anchored above player nameplates. It links Last.fm listening state with in-game social context while keeping publication explicit and bounded.

## Initial user journey

1. Install XIV.fm through Dalamud.
2. Connect a Last.fm account through a browser authorization flow.
3. See the current track locally above the player's character.
4. Select one visibility policy.
5. Optionally see matching XIV.fm users in the same game location when they are within the configured yalm range.

## Visibility

### Private

The track is shown only to the local user. No social presence is published. The server may still retrieve the track to provide the local card.

### Public

The track is included in a shared, time-bounded snapshot for the user's current game location. Other clients render it only when the matching character is loaded and within range.

### Custom Relays

A user can create an arbitrarily named, invitation-based Relay and select one or more joined Relays as the listening audience. A Relay has one owner and members. It has no role hierarchy.

The owner can rename or delete the Relay, issue and revoke invitations, inspect membership, and kick a member. Members can consume Relay presence and leave.

## Location and distance

The backend scope must distinguish current world, territory, and an instance/ward identifier when Dalamud provides one. A territory ID alone is insufficient.

The server never needs character coordinates. It returns relevant location snapshots; the plugin matches loaded player objects and performs the final distance check.

- Default distance: 8 yalms.
- Planned configurable range: 1–20 yalms.
- Cards outside the range are not rendered.

## Account activity terminology

- **Active and playing:** the plugin heartbeat is fresh and Last.fm reports a current track.
- **Active with no current track:** the plugin heartbeat is fresh but Last.fm reports no current track.
- **Offline:** the heartbeat expired, the character logged out, or the plugin/game stopped.

Offline linked accounts are not polled. FFXIV AFK state alone does not stop polling because an AFK player may still be listening.

## Non-goals for the first stable release

- Last.fm scrobbling or write operations.
- Friend-list or Free Company integrations.
- Relay role hierarchies.
- Public player search or a global presence directory.
- Permanent listening history.
- Final visual customization before behavior is stable.
- Federation or self-hosted private Relay servers.

## Success criteria

- A card reliably follows the intended player/nameplate without crashing or obstructing gameplay.
- Account linking proves control of a Last.fm account without exposing provider secrets.
- Last.fm request volume remains below a configured global budget.
- Private data cannot appear in public or unauthorized Relay snapshots.
- Public/Relay map results are shared across clients rather than rebuilt per viewer.
- The system remains functional under upstream errors by serving explicitly stale cached state.
