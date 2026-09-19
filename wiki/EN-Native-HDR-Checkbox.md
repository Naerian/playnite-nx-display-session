# Why the native HDR checkbox remains

Playnite Desktop (and some Fullscreen flows) still shows the built-in **Enable HDR** / `EnableSystemHdr` game option. Display Manager cannot hide that checkbox: there is **no public Playnite SDK API** to remove or replace native game-detail controls.

## What NX does instead

1. **Setup / Maintenance** can clear `EnableSystemHdr` library-wide (with a backup for reverse restore).
2. On each game launch, if the flag is on again, NX clears it and notifies once — so native restore and NX restore do not stack.
3. HDR for the session is owned by Display Manager policies and per-game overrides ([HDR](EN-HDR)).

## What you should use

| Control | Use |
|---------|-----|
| Display Manager global HDR policy | Default for the library |
| Context menu → Display Manager → HDR | Per-game Inherit / Force on / Force off / Do not touch |
| Native Enable HDR checkbox | Prefer leave cleared; NX will clear it if it comes back |

## FAQ

**Will the checkbox ever disappear?**  
Only if Playnite adds an SDK or UI hook for it. Until then, NX documents and disarms the flag rather than pretending the control is gone.

**I turned native HDR on by mistake.**  
Clear via Maintenance, or launch once so NX clears and notifies; then set the NX override/policy you want.

**Does NX fight native restore?**  
That was the bug with ACM + trusted GET. NX writes SDR on exit and clears the native flag so both paths do not leave HDR stuck on.
