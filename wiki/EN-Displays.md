# Displays

Display Manager enumerates active Windows displays and applies a session topology so the game can target the screen you care about (typically the TV in an HTPC couch setup).

## Identity

Displays are identified primarily via **EDID** and CCD path info, not fragile device instance paths alone. When Windows exposes enough data, identity survives cable or GPU path changes better than name-only matching.

If a dock, splitter, or adapter strips EDID, matching may fall back to weaker keys — Overview and Displays settings show what the plugin resolved.

## Target display

In Settings → Displays (and related Overview controls):

- Pick which active display is the **game target**.
- Confirm secondary screens remain as you expect after apply/restore.
- Use a short launch/quit cycle after changing the target to validate restore.

## Refresh rate

When configured, Display Manager can change refresh rate on the session path (`ChangeDisplaySettingsEx`). Restore returns the previous mode with the topology snapshot / RestoreHost lease.

## What restore covers

- Active topology (which paths are enabled / primary as snapshotted).
- HDR write-off for session targets (see [HDR](EN-HDR)).
- Refresh rate when it was changed for the session.

## What restore does not cover (v1)

- Moving Playnite’s own Fullscreen window between monitors.
- Per-game custom resolutions beyond the applied session topology.
- Upscalers, VRR, CEC.

## Diagnostics

- Overview lists resolved displays and the selected game’s effective HDR plan.
- RestoreHost log: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
- If restore appears stuck, check that RestoreHost is not held by a crashed lease (log + Task Manager).
