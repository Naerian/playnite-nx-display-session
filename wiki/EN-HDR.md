# HDR

Display Manager **owns HDR** when installed. It plans an on/off write for the session and restores by writing HDR **off** — it does not trust Windows GET under Automatic Color Management.

Deep dive for developers: [docs/HDR-AND-ACM.md](https://github.com/Naerian/playnite-nx-display-session/blob/main/docs/HDR-AND-ACM.md).

## Global policies

| Policy | Behavior |
|--------|----------|
| **1 — Do not touch** | No HDR write on start; still restores topology. |
| **2 — Always on** | Write HDR on for capable active targets at game start. |
| **3 — Metadata** | Write HDR on only when local Features (and optionally Tags) match configured names. No network on launch. |

Default metadata names: `HDR`, `HDR10`, `Dolby Vision`, `Auto HDR`, `HDR10+`.

## Per-game override

Context menu → Display Manager → HDR:

- **Keep global settings** — use the global HDR policy.
- **Force on** — write HDR on.
- **Force off (SDR)** — write HDR off.
- **Do not touch** — skip HDR writes for this game.

Override always wins over the global policy.

## Session flow

1. Resolve effective plan (global + override + metadata).
2. Capture topology snapshot; arm RestoreHost lease.
3. **Write** HDR on or off as planned (CCD advanced color / HDR state APIs).
4. On stop / cancel / Playnite exit: restore topology and **write HDR off** for those targets.

The snapshot stores write intent (`enable: false` on restore). It does not ask Windows what HDR “is”.

## Overview honesty

When ACM may apply, Overview shows HDR as **Unknown**. That is not a bug — readback lies. Use **Win+Alt+B** or Settings → HDR → **Write HDR off now** for a forced SDR write.

## Related

- [ACM troubleshooting](EN-Troubleshooting-ACM)
- [Native HDR checkbox](EN-Native-HDR-Checkbox)
