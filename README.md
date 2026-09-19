# Playnite NX Display Manager

Display Manager applies a Windows display profile when a game starts and restores the previous desktop state when the game stops.

Designed for HTPC / couch setups (TV plus another screen): choose which display a game uses and HDR policy — then bring the desktop back. Night Light is intentionally not managed in v1 (no supported Win10+Win11 API).

## Status

Early (`0.1.0`). Core features and user wiki (EN/ES) are in place. Installer / add-on catalog polish is next.

## Requirements

- Windows.
- Playnite 10.x.
- Playnite SDK 6.16 compatible runtime.

## Wiki

- [Home](https://github.com/Naerian/playnite-nx-display-session/wiki)
- [Overview (EN)](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-Overview)
- [Descripción general (ES)](https://github.com/Naerian/playnite-nx-display-session/wiki/ES-Descripcion-General)
- [HDR (EN)](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-HDR) · [HDR (ES)](https://github.com/Naerian/playnite-nx-display-session/wiki/ES-HDR)
- [ACM troubleshooting](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-Troubleshooting-ACM) · [Solución ACM](https://github.com/Naerian/playnite-nx-display-session/wiki/ES-Solucion-de-Problemas-ACM)
- [Native HDR checkbox](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-Native-HDR-Checkbox) · [Checkbox nativo](https://github.com/Naerian/playnite-nx-display-session/wiki/ES-Checkbox-HDR-nativo)

## Technical docs (repo)

- [HDR and ACM (EN)](docs/HDR-AND-ACM.md)
- [HDR y ACM (ES)](docs/HDR-Y-ACM.es.md)
- [Night Light cut (EN)](docs/NIGHT-LIGHT.md)
- [Luz nocturna recortada (ES)](docs/NIGHT-LIGHT.es.md)
- [Theme API v1 (EN)](docs/THEME-API.md)
- [Theme API v1 (ES)](docs/THEME-API.es.md)
- [Audio Switcher hook (EN)](docs/AUDIO-SWITCHER.md)
- [Hook Audio Switcher (ES)](docs/AUDIO-SWITCHER.es.md)

Source pages for the GitHub wiki live under [`wiki/`](wiki/).

## Support

- Author: Narian
- [Ko-fi](https://ko-fi.com/naerian)

## Build

```powershell
.\package.ps1
```

Requires `dotnet` SDK and Playnite Toolbox at `C:\Playnite\Toolbox.exe` (override with `-ToolboxPath`).
