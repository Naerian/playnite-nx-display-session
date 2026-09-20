# Overview

Display Manager is a Playnite GenericPlugin that owns **Windows display profiles, HDR, resolution, and refresh rate** for game sessions. On game start it applies the resolved profile; on stop, cancel, or Playnite exit it restores the previous desktop state via a durable RestoreHost lease.

## What it does

- Lists active displays with EDID-based identity when Windows exposes enough data.
- Lets you choose a **primary display for games** through named **display profiles**. The built-in defaults are **Solo TV** for couch/TV play and **PC / Desktop** for keeping the normal desktop layout.
- Handles missing displays with Windows primary, fallback display, or notify-and-continue behavior.
- Owns **HDR** with global policy, display-profile defaults, and per-game/per-platform overrides.
- Optionally applies **resolution** and **refresh rate** after the display profile, then waits the configured **settle delay** before continuing.
- Exposes Fullscreen theme controls and `PluginSettings` through SourceName `DisplayManager`.

## Priority

Display Manager resolves settings in this order: game profile, platform profile, default display profile, then global settings. `Keep global settings` in a game menu clears that override so the next layer applies.

## Design priorities

Stability and honesty over clever UI. Under Automatic Color Management (ACM), Windows HDR readback is unreliable, so Display Manager writes the intended HDR state and may show HDR as **Unknown**.

Night Light is not managed in v1: there is no supported public Win10+Win11 API that works reliably across both OS lines.

## Out of scope (v1)

Upscalers, VRR, CEC, process injection, Harmony remotes, and audio device switching.

Continue with [Installation & Quick Start](EN-Installation-and-Quick-Start).
