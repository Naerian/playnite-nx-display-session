# Why the native HDR checkbox remains

Playnite Desktop (and some Fullscreen flows) still shows the built-in **Enable HDR** / `EnableSystemHdr` game option. Display Manager cannot hide that checkbox: there is **no public Playnite SDK API** to remove or replace native game-detail controls.

## What NX does instead

1. **Setup / Maintenance** can clear `EnableSystemHdr` library-wide (with a backup for reverse restore) — skip this when using the **Playnite native** global HDR policy.
2. On each game launch, if the flag is on again **and** the global policy is not Playnite native, NX clears it and notifies once — so native restore and NX restore do not stack.
3. HDR for the session is owned by Display Manager policies and per-game overrides ([HDR](EN-HDR)), unless you chose **Playnite native**, which defers to `EnableSystemHdr`.

## What you should use

| Control | Use |
|---------|-----|
| Display Manager global HDR policy | Default for the library (including **Playnite native** if you want the built-in checkbox) |
| Context menu → Display Manager → HDR | Per-game Inherit / Force on / Force off / Do not touch |
| Native Enable HDR checkbox | Prefer leave cleared when NX owns HDR; keep and use it when the global policy is **Playnite native** |

## FAQ

**Will the checkbox ever disappear?**  
Only if Playnite adds an SDK or UI hook for it. Until then, NX documents and (by default) disarms the flag rather than pretending the control is gone.

**I turned native HDR on by mistake.**
If NX owns HDR: clear via Maintenance, or launch once so NX clears and notifies; then set the NX override/policy you want.
If you *want* native HDR: set the global policy to **Playnite native** so NX stops clearing the flag.

**Does NX fight native restore?**  
That was the bug with ACM + trusted GET. When NX owns HDR it writes SDR on exit and clears the native flag so both paths do not leave HDR stuck on. With **Playnite native**, NX stays out of HDR writes and flag clearing.
