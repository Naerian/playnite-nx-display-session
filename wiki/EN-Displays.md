# Displays

Display Manager enumerates active Windows displays and applies a session topology so the game can target the screen you care about (typically the TV in an HTPC couch setup).

## Identity

Displays are identified primarily via **EDID** and CCD path info, not fragile device instance paths alone. When Windows exposes enough data, identity survives cable or GPU path changes better than name-only matching.

If a dock, splitter, or adapter strips EDID, matching may fall back to weaker keys — Overview and Displays settings show what the plugin resolved.

## Primary display for games

In Settings → Displays:

- Manage **topology profiles** (named packages). One is the **default for launch**.
- Each profile sets the **primary display for games** (or Keep Windows default), whether to **turn off other displays**, and what to do if the preferred display is **missing** (Windows primary, fallback display, or notify and continue).
- Rename displays for Playnite, use **Identify**, and preview with the short trial buttons.

## Per-game and platform profiles

Priority is always:

1. **Game profile** (context menu)
2. **Platform profile** (Settings → General → Game Profiles → Platform profiles)
3. **Default topology profile** / global HDR and refresh settings

Context menu → Display Manager → Display / HDR / Refresh rate: **Keep global settings** clears the game override so platform then default apply.

## Refresh rate

When configured under Settings → General → Refresh rate, Display Manager can change refresh rate on the play-primary path (`ChangeDisplaySettingsEx`). Rates listed are those reported for that display at its current resolution. Restore returns the previous mode with the topology snapshot / RestoreHost lease.

Per-game override: context menu → Display Manager → Refresh rate → **Keep global settings** or a specific policy/rate.

## What restore covers

- Active topology (which paths are enabled / primary as snapshotted).
- HDR write-off for session targets (see [HDR](EN-HDR)).
- Refresh rate when it was changed for the session.

## What restore does not cover (v1)

- Moving Playnite’s own Fullscreen window between monitors.
- Per-game custom resolutions beyond the applied session topology.
- Upscalers, VRR, CEC.
- Audio device switching (use [Audio Switcher](https://github.com/Naerian/playnite-nx-audio-switcher) if you need that).

## Diagnostics

- Overview lists resolved displays and session policies.
- RestoreHost log: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
- If restore appears stuck, check that RestoreHost is not held by a crashed lease (log + Task Manager).
