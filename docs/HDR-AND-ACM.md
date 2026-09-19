# HDR and Automatic Color Management (ACM)

Display Manager **owns HDR** when installed. It does not trust Windows HDR status readback under Automatic Color Management.

## Why readback lies

On Windows 11 (especially 24H2) with **Automatically manage apps colors** (ACM), `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` often reports a state that does not match what you see. Playnite's native `EnableSystemHdr` can leave HDR on after a game because restore relied on that read.

## What Display Manager does

1. **On game start** (global policy “any game”): capture topology snapshot, arm RestoreHost, **write** advanced color / HDR **on** for the primary when supported.
2. **On game stop / cancel / Playnite exit**: **write HDR off** for those targets (and restore topology). The snapshot stores the write intent (`enable: false`) — it does not ask Windows what HDR “is”.
3. Overview shows HDR as **Unknown** when ACM may apply. That is intentional honesty.

## Diagnostics

- Visual check: **Win+Alt+B** (Xbox Game Bar HDR toggle) to see what the display actually does.
- Settings → HDR → **Write HDR off now** forces SDR by write on capable active displays.
- RestoreHost log: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`

## Spanish

Ver [HDR-Y-ACM.es.md](HDR-Y-ACM.es.md).
