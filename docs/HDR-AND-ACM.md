# HDR and Automatic Color Management (ACM)

Display Manager **owns HDR** when installed. It does not trust Windows HDR status readback under Automatic Color Management.

## Why readback lies

On Windows 11 (especially 24H2) with **Automatically manage apps colors** (ACM), `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` often reports a state that does not match what you see. Playnite's native `EnableSystemHdr` can leave HDR on after a game because restore relied on that read.

## What Display Manager does

1. **On game start**: resolve the effective plan (global policy + per-game override + optional Features/Tags match), capture topology snapshot, arm RestoreHost, then **write** HDR on or off as planned.
2. **On game stop / cancel / Playnite exit**: **write HDR off** for those targets (and restore topology). The snapshot stores the write intent (`enable: false`) — it does not ask Windows what HDR “is”.
3. Overview shows HDR as **Unknown** when ACM may apply. That is intentional honesty.
4. **Policy 3 (metadata)**: matches local Playnite `Game.Features` (and optionally `Tags`) against configurable names — default `HDR`, `HDR10`, `Dolby Vision`, `Auto HDR`, `HDR10+`. No network on launch.
5. **Per-game override** (context menu → Display Manager → HDR): Inherit / Force on / Force off (SDR) / Do not touch. Override always wins over the global policy.
6. **Native EnableSystemHdr**: setup wizard (and Maintenance) can clear Playnite’s flag library-wide (backup kept for reverse). On each launch, if the flag is on again, NX clears it and notifies once — so native and NX restores do not stack. The native checkbox may still appear; there is no public SDK API to hide it.

## Diagnostics

- Visual check: **Win+Alt+B** (Xbox Game Bar HDR toggle) to see what the display actually does.
- Settings → HDR → **Write HDR off now** forces SDR by write on capable active displays.
- Overview → **Selected game (HDR)** previews metadata match and the effective plan for the library selection.
- Overview / Maintenance → native `EnableSystemHdr` count and clear/restore.
- RestoreHost log: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`

## Spanish

Ver [HDR-Y-ACM.es.md](HDR-Y-ACM.es.md).
