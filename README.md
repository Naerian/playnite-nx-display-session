# Playnite NX Display Manager

Display Manager applies a Windows display profile when a game starts and restores the previous desktop state when the game stops.

Designed for HTPC / couch setups (TV plus another screen): choose which display a game uses and HDR policy — then bring the desktop back. Night Light is intentionally not managed in v1 (no supported Win10+Win11 API).

## Status

Early (`0.1.0`). Chrome, displays, RestoreHost, topology trial, HDR, EnableSystemHdr migration, optional refresh rate (Hz), and Night Light cut are in place. Menus / Theme API polish is next.

## Documentation

- [HDR and ACM (EN)](docs/HDR-AND-ACM.md)
- [HDR y ACM (ES)](docs/HDR-Y-ACM.es.md)
- [Night Light cut (EN)](docs/NIGHT-LIGHT.md)
- [Luz nocturna recortada (ES)](docs/NIGHT-LIGHT.es.md)

## Requirements

- Windows.
- Playnite 10.x.
- Playnite SDK 6.16 compatible runtime.

## Documentation

- [Wiki (EN)](https://github.com/Naerian/playnite-nx-display-session/wiki)
- [Wiki (ES)](https://github.com/Naerian/playnite-nx-display-session/wiki)

## Support

- Author: Narian
- [Ko-fi](https://ko-fi.com/naerian)

## Build

```powershell
.\package.ps1
```

Requires `dotnet` SDK and Playnite Toolbox at `C:\Playnite\Toolbox.exe` (override with `-ToolboxPath`).
