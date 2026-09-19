# ACM troubleshooting

On Windows 11 (especially 24H2) with **Automatically manage apps colors** (ACM), `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` often reports a state that does not match what you see. Playnite’s native `EnableSystemHdr` can leave HDR on after a game because restore trusted that read.

Display Manager never trusts that GET for restore.

## Symptoms

- Windows Settings says HDR is off, but the TV still looks like HDR (or the reverse).
- After a game, desktop stays in HDR even though Playnite “restored”.
- Overview shows HDR as **Unknown**.

## What to do

1. Prefer **visual confirmation**: **Win+Alt+B** (Xbox Game Bar HDR toggle) and watch the display.
2. In Display Manager Settings → HDR, use **Write HDR off now** to force SDR by write on capable active displays.
3. Clear native `EnableSystemHdr` via Overview / Maintenance so Playnite and NX do not stack restores ([Native HDR checkbox](EN-Native-HDR-Checkbox)).
4. Check RestoreHost log: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
5. If ACM is optional for your workflow, try toggling **Automatically manage apps colors** in Windows color settings and retest — NX still writes; ACM mainly poisons *readback*.

## Why Unknown is correct

Showing a confident On/Off from a lying API would be worse than Unknown. The effective **plan** for the selected game is still previewed on Overview (metadata match + override).

## Related

- [HDR](EN-HDR)
- Technical notes: [HDR-AND-ACM.md](https://github.com/Naerian/playnite-nx-display-session/blob/main/docs/HDR-AND-ACM.md)
