# Displays

Display Manager separates **connected display inventory** from **display profiles**.

Settings -> Displays is for connected displays only: resolved identity, custom names, visibility in Display Manager, and Identify actions. Profile behavior lives under Settings -> General.

## Display profiles

In Settings -> General -> Display profiles, each profile can define:

- Primary display for games, or Keep Windows default.
- Whether other displays turn off when a game starts.
- Missing-display policy and fallback display.
- HDR, refresh rate, and resolution defaults.

The built-in defaults are **Solo TV** for a TV-only game session and **PC / Desktop** for the normal desktop layout. One profile is the **launch default** unless a game or platform profile overrides it.

## Missing displays

Settings -> General -> Missing display decides what happens if the preferred play display in the selected display profile is not connected: use Windows primary, use a fallback display, or notify and continue.

## Resolution and refresh rate

Resolution for games runs after the display profile is applied and before refresh rate/HDR. Refresh rates are those reported by Windows for the play-primary display at the current resolution.

The **settle delay** in General -> Options waits after display layout, resolution, refresh rate, or HDR changes before the game continues. Use it for displays or AVRs that need extra time to stabilize.

## Priority

1. Game profile from the game context menu.
2. Platform profile from Settings -> General -> Platform profiles.
3. Default display profile and global settings.

## Restore coverage

Restore returns the previous display layout, resolution/refresh changes made for the session, and HDR write-off for session targets. RestoreHost keeps this protection alive if Playnite exits unexpectedly.
