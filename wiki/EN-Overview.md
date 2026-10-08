# Overview

Display Manager is a Playnite GenericPlugin that owns **Windows display layout, resolution, refresh rate, and HDR** for game sessions. On game start it applies the resolved settings; on stop, cancel, or Playnite exit it restores the previous desktop state via a durable RestoreHost lease.

Built for living-room gaming: a PC on the sofa connected to a TV, or setups that switch between a TV and another screen.

## What it does

- Lists active displays with EDID-based identity when Windows exposes enough data.
- Lets you choose separate **primary displays for Desktop and Fullscreen**, and edit each mode’s launch settings (HDR, refresh, resolution, turn-off-others, missing display). Built-in profiles **Solo TV** / **PC / Desktop** remain for overrides. Entering **fullscreen** applies the Fullscreen primary and keeps that layout for the session (restored when you return to desktop).
- Handles missing displays with Windows primary, fallback display, or notify-and-continue behavior.
- Owns **HDR** with global policy, display-profile defaults, and per-game/per-platform overrides.
- Optionally applies **resolution** and **refresh rate** after the display profile, then waits the configured **settle delay** before continuing.
- Exposes Fullscreen theme controls and `PluginSettings` through SourceName `DisplayManager`.

## Priority

Display Manager resolves settings in this order: game profile, platform profile, then launch settings for the current Playnite mode (Desktop or Fullscreen). `Keep global settings` in a game menu clears that override so the next layer applies.

## Design priorities

Stability and honesty over clever UI. Under Automatic Color Management (ACM), Windows HDR readback is unreliable, so Display Manager writes the intended HDR state and may show HDR as **Unknown**.

Night Light is not managed in v1: there is no supported public Win10+Win11 API that works reliably across both OS lines.

## Out of scope (v1)

Upscalers, VRR, CEC, process injection, Harmony remotes, and audio device switching.

Continue with [Installation & Quick Start](EN-Installation-and-Quick-Start).
