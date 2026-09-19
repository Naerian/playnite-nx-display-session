# Overview

Display Manager is a Playnite GenericPlugin that owns **Windows display topology and HDR** for game sessions. On game start it applies your profile; on stop, cancel, or Playnite exit it restores the previous desktop state via a durable RestoreHost lease.

## What it does

- Lists active displays with EDID-based identity (stable across cable/GPU path changes when Windows exposes enough data).
- Lets you choose a **target display** for the game session (TV-first HTPC workflows).
- Owns **HDR** with three global policies, per-game overrides, and optional Features/Tags metadata matching.
- Changes **refresh rate** when configured (ChangeDisplaySettingsEx path).
- Arms **RestoreHost** so topology/HDR restore survives Playnite crashes.
- Exposes a **Theme API** (`SourceName` `DisplayManager`) and a Desktop top-panel shortcut.
- Soft-hooks **Audio Switcher** when installed (reflection only; never hard-depends).

## Design priorities

Stability and honesty over clever UI. Under Automatic Color Management (ACM), Windows HDR *readback* is unreliable — Display Manager **writes** the intended HDR state and restores by writing SDR off. Overview may show HDR as **Unknown**; that is intentional.

Night Light is **not** managed in v1: there is no supported public Win10+Win11 API that works reliably across both OS lines.

## Out of scope (v1)

Resolution-per-game beyond current topology apply, upscalers, VRR, moving Playnite Fullscreen windows, CEC, process injection, Harmony remotes.

## Important limitations

- HDR status under ACM is not trusted for GET; use visual checks (e.g. Win+Alt+B).
- Display identity depends on EDID / Windows CCD; some docks and adapters rename paths.
- Audio Switcher integration is best-effort when that extension is present.

Continue with [Installation & Quick Start](EN-Installation-and-Quick-Start).
