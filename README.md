# Playnite NX Display Manager

Display Manager applies a Windows display profile when a game starts and restores the previous desktop state when the game stops.

Designed for HTPC / couch setups (TV plus another screen): choose which display a game uses, HDR policy, and optional Night Light / refresh changes — then bring the desktop back.

## Status

Early (`0.1.0`). Settings chrome, display enumeration, RestoreHost lease, and topology trial (primary / opt-in others-off with auto-restore) are in place. HDR ownership lands next.

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
