# Playnite NX Display Manager

Display Manager is a Playnite extension that applies Windows display settings (layout, resolution, refresh rate, and HDR) when a game starts and restores the previous desktop state when the game stops.

It is built for living-room gaming — a PC on the sofa connected to a TV, or setups that switch between a TV and another screen. Choose the primary display, set launch defaults for HDR and related options, override them per game when needed, then bring the desktop back.

## Features

- Enumerate active displays with EDID-based identity that stays stable across cable or GPU path changes when Windows exposes enough data.
- Choose a primary display for games, or keep the Windows default primary untouched.
- Optionally turn off other displays when a game launches, with a short display layout trial and automatic restore.
- Rename displays for Playnite and identify each monitor with an on-screen label.
- Own HDR for game sessions with global policies: leave Windows alone, always on, or Features/Tags metadata matching.
- Override display profile, HDR, resolution, refresh rate, and play display per game from the game context menu.
- Change resolution and refresh rate on the play-primary path when configured, then wait the configured settle delay before continuing.
- Restore display layout, resolution/refresh changes, and HDR write-off when the game stops, is cancelled, or Playnite exits.
- Arm RestoreHost so restore survives an unexpected Playnite exit.
- Clear Playnite's native Enable HDR checkbox when Display Manager owns the session so restores do not fight each other.
- Show an honest Overview when Automatic Color Management may make HDR readback unreliable.
- Show the primary display in Playnite's Desktop top bar, with icon, text, or both.
- Integrate DisplayList, HdrStatus, ActiveProfile, SessionStatus, DisplaysSummary, and OpenSettingsButton controls into Fullscreen themes.
- Use Playnite localization resource dictionaries with English fallback.
- Choose which informational notifications are shown.

## Requirements

- Windows.
- Playnite 10.x.
- Playnite SDK 6.16 compatible runtime.

## Installation

### Playnite add-on browser

Search for **Display Manager** in Playnite's add-on browser, or use this direct link:

`playnite://playnite/installaddon/PlayniteDisplayManager_9c2e4a71-b8d3-4f6a-a1c5-0e7d92f3b846`

### Manual installation

1. Download the latest `.pext` from the [GitHub releases page](https://github.com/Naerian/playnite-nx-display-session/releases/latest).
2. Open the `.pext` file or drag it into Playnite.
3. Restart Playnite if requested.

## Quick Start

Open:

`Add-ons > Extension settings > Generic > Display Manager`

The settings window opens on **Overview**, then **Displays**, **General**, **Notifications**, **Advanced**, and **About**. Use **Displays** only for connected display inventory: names, identity, visibility, and Identify. Under **General**, configure Options, HDR, refresh rate, resolution for games, missing-display fallback, display profiles, game profiles, and platform profiles. Display profiles include the built-in **Solo TV** and **PC / Desktop** defaults, and one profile is the launch default unless a game or platform override wins. Per-game overrides for display profile, HDR, resolution, refresh rate, and display live in each game's context menu under **Display Manager**; **Keep global settings** clears that override. The settle delay in General -> Options waits after display, resolution, refresh, or HDR changes before the game continues.

Night Light is not managed in v1: Windows does not expose a stable public API that works reliably across Windows 10 and Windows 11.

To open settings quickly:

- Desktop: click the Display Manager top-bar button, or `Main menu > Extensions > Display Manager`.
- Per game: open the game's context menu and select `Display Manager`.

## Documentation

- [Documentation in English](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-Overview)
- [Documentacion en espanol](https://github.com/Naerian/playnite-nx-display-session/wiki/ES-Descripcion-General)
- [HDR](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-HDR)
- [Displays](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-Displays)
- [ACM troubleshooting](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-Troubleshooting-ACM)
- [Native HDR checkbox](https://github.com/Naerian/playnite-nx-display-session/wiki/EN-Native-HDR-Checkbox)

## Fullscreen Theme Integration

Theme developers can use bundled controller-friendly controls:

```xml
<ContentControl x:Name="DisplayManager_DisplayList" />
<ContentControl x:Name="DisplayManager_HdrStatus" />
<ContentControl x:Name="DisplayManager_ActiveProfile" />
<ContentControl x:Name="DisplayManager_SessionStatus" />
<ContentControl x:Name="DisplayManager_DisplaysSummary" />
<ContentControl x:Name="DisplayManager_OpenSettingsButton" />
```

Display Manager also exposes `PluginSettings` with `SourceName` `DisplayManager`, `SettingsRoot` `Theme`, and `ApiVersion` `1.1.0`:

```xml
<TextBlock Text="{PluginSettings Plugin=DisplayManager, Path=Theme.DisplaysSummary}" />
<TextBlock Text="{PluginSettings Plugin=DisplayManager, Path=Theme.ActiveDisplayProfileName}" />
```

Useful paths include `Theme.PrimaryDisplayName`, `Theme.PrimaryDisplayAlias`, `Theme.ConnectedDisplayCount`, `Theme.HdrPolicyLabel`, `Theme.HdrStatusLabel`, `Theme.DisplaysSummary`, `Theme.ActiveDisplayProfileName`, `Theme.ActiveProfileSourceLabel`, `Theme.SessionActive`, `Theme.SessionGameName`, and `Theme.OpenSettingsCommand`. Capability flags include `Theme.SupportsDisplayList`, `Theme.SupportsHdrStatus`, `Theme.SupportsActiveProfile`, `Theme.SupportsSessionStatus`, `Theme.SupportsDisplaysSummary`, and `Theme.SupportsOpenSettings`.

The repository and release package include a commented example at [`Examples/FullscreenThemeIntegration.xaml`](Examples/FullscreenThemeIntegration.xaml). Deeper notes live in [`docs/THEME-API.md`](docs/THEME-API.md) and the wiki theme pages.

## Localization

The plugin uses Playnite localization resource dictionaries under `Localization/`. Unsupported locales fall back to English. Community translation contributions are welcome.

## Support

If you find this project useful and want to support its development, consider buying me a coffee!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/naerian)
