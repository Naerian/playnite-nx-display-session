# Displays

Display Manager separates **connected display inventory** from **display profiles**.

Settings -> Displays is for connected displays only: resolved identity, custom names, visibility in Display Manager, Identify actions, and the optional fullscreen layout. Launch behavior (HDR, refresh rate, resolution, missing display) lives under Settings -> When a game launches. Game and platform profiles live under Settings -> General.

## Display profiles

In Settings -> General -> Display profiles, each profile can define:

- Primary display for games, or Keep Windows default.
- Whether other displays turn off when a game starts.
- Missing-display policy and fallback display.
- HDR, refresh rate, and resolution defaults.

On Settings → Displays you choose a **primary display for Desktop** and a **primary display for Fullscreen** from your connected screens (or Keep Windows default). Playnite’s window can stay on any monitor; these pickers only steer where games go. Built-in profiles (**Solo TV**, **PC / Desktop**) remain available for game/platform overrides. Game and platform profiles still override the mode primary.

## Fullscreen layout

When you open Playnite **Fullscreen**, Display Manager applies the **Fullscreen** primary (and that mode’s turn-off-others / missing-display settings from **When a game launches**). The layout stays for the whole fullscreen session so games do not switch monitors back and forth. Returning to desktop restores the previous layout.

Typical living-room setup: Desktop primary = desk monitor (or Windows default); Fullscreen primary = TV; turn other displays off under Fullscreen launch settings; desk monitor as fallback if the TV is unplugged or asleep.

## Game context menu

Library context menu → **Display Manager**:

- **Display** — inherit, keep Windows default, or pick a connected display. Custom names from Settings appear here.
- **Other displays** — inherit, turn off other displays, or keep them on for that game.

## Missing displays

Settings -> When a game launches -> Missing display decides what happens if the preferred play display in the selected display profile is not connected: use Windows primary, use a fallback display, or notify and continue.

## Resolution and refresh rate

Resolution for games runs after the display profile is applied and before refresh rate/HDR. Refresh rates are those reported by Windows for the play-primary display at the current resolution.

The **settle delay** in General -> Options waits after display layout, resolution, refresh rate, or HDR changes before the game continues. Use it for displays or AVRs that need extra time to stabilize.

## Priority

1. Game profile from the game context menu.
2. Platform profile from Settings -> General -> Platform profiles.
3. Launch settings for the current Playnite mode (Desktop or Fullscreen): primary display, turn-off-others, missing display, HDR, refresh rate, and resolution.

## Restore coverage

Restore returns the previous display layout, resolution/refresh changes made for the session, and HDR write-off for session targets. RestoreHost keeps this protection alive if Playnite exits unexpectedly.
