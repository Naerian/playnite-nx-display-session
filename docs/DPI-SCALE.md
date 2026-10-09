# Display scale / DPI (Fullscreen)

## Decision (go, experimental)

Ship as an **opt-in** Advanced setting for Playnite Fullscreen mode sessions only:

**Force 100% display scale when entering Fullscreen**

Default: **off**.

## Why

Living-room TVs often run at 125% or 150% Windows scaling. Playnite Fullscreen can look wrong or misbehave at those scales. Users asked Display Manager to force 100% for the Fullscreen session and restore afterward — the same apply/restore contract used for topology.

## How it works

1. On Fullscreen mode begin, read the current scale of the Fullscreen primary via undocumented CCD `DisplayConfigGetDeviceInfo` type **-3**.
2. If not already 100% (and 100% is supported), write the previous percent into `DisplaySnapshot.DpiRestoreWrites`, arm RestoreHost, then set scale to 100% via type **-4**.
3. On mode end or crash restore, `TryRestoreSnapshot` restores topology/HDR and then applies the DPI restore writes.

## Risks (same class as Night Light)

- Not a public Microsoft API (community reverse-engineering; used by tools such as SetDPI).
- Changing scale while Playnite is already running may leave UI layout imperfect until a theme refresh or restart.
- Future Windows builds could change the packet layout; struct size checks log a warning and skip apply.

## What we deliberately did not ship

- Arbitrary scale picker (125/150/…)
- Per-game scale overrides
- Scale changes during game sessions (only Fullscreen mode enter/exit)

## Manual test checklist

- [ ] TV alone at 150% → enter Fullscreen → scale 100% → leave → scale 150%
- [ ] TV + PC monitor → only Fullscreen primary changes
- [ ] Kill Playnite while in Fullscreen with the option on → RestoreHost restores previous scale
- [ ] Option off → no scale change

## Spanish

Ver [DPI-SCALE.es.md](DPI-SCALE.es.md).
