# Installation & Quick Start

## Install

1. Download the latest `.pext` from [Releases](https://github.com/Naerian/playnite-nx-display-session/releases/latest).
2. In Playnite: **Add-ons → Install from file** and select the package.
3. Restart Playnite if prompted.
4. Open **Add-ons → Extensions → Display Manager → Settings**.

## First-run checklist

1. **Displays** — confirm your TV and secondary screen appear; pick the default target for games.
2. **HDR** — choose a global policy (see [HDR](EN-HDR)):
   - **1** Always leave HDR alone (do not touch).
   - **2** Always write HDR on for capable targets when a game starts.
   - **3** Metadata: turn HDR on only when Features/Tags match (default names: `HDR`, `HDR10`, `Dolby Vision`, `Auto HDR`, `HDR10+`).
3. **Native EnableSystemHdr** — the setup/Maintenance flow can clear Playnite’s library flag so native restore does not fight NX. See [Native HDR checkbox](EN-Native-HDR-Checkbox).
4. Launch a short game session, quit, and confirm the desktop topology and SDR return.

## Per-game HDR override

Library context menu → **Display Manager → HDR**: Inherit / Force on / Force off (SDR) / Do not touch. Override always wins over the global policy.

## Verify restore

- After exit, desktop layout should match pre-game.
- HDR should be off when NX wrote SDR (do not trust Settings readback under ACM — use [ACM troubleshooting](EN-Troubleshooting-ACM)).
- RestoreHost log (if needed): `%TEMP%\PlayniteDisplayManager-RestoreHost.log`

## Build from source

```powershell
.\package.ps1
```

Requires `dotnet` SDK and Playnite Toolbox (`C:\Playnite\Toolbox.exe`, or `-ToolboxPath`).
