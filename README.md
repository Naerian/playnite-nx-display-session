# Playnite NX Display Manager

Display Manager is a Playnite extension that applies a Windows display and HDR profile when a game starts and restores the previous desktop state when the game stops.

It is designed for HTPC and couch setups that regularly move between a TV and another screen: choose which display a game uses, set a global HDR policy, optionally override per game, then bring the desktop back.

## Features

- Enumerate active displays with EDID-based identity that stays stable across cable or GPU path changes when Windows exposes enough data.
- Choose a primary display for games, or keep the Windows default primary untouched.
- Optionally turn off other displays when a game launches, with a short topology trial and automatic restore.
- Rename displays for Playnite and identify each monitor with an on-screen label.
- Own HDR for game sessions with global policies: leave Windows alone, always on, or Features/Tags metadata matching.
- Override HDR, refresh rate, and play display per game from the game context menu.
- Change refresh rate on the play-primary path when configured, using rates reported at the current resolution.
- Restore topology, refresh rate, and HDR write-off when the game stops, is cancelled, or Playnite exits.
- Arm RestoreHost so restore survives an unexpected Playnite exit.
- Clear Playnite's native Enable HDR checkbox when Display Manager owns the session so restores do not fight each other.
- Show an honest Overview when Automatic Color Management may make HDR readback unreliable.
- Show the primary display in Playnite's Desktop top bar, with icon, text, or both.
- Integrate display list and HDR status controls into Fullscreen themes.
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

The settings window opens on **Overview**, then **Displays**, **General**, **Game Profiles**, **Notifications**, **Advanced**, and **About**. Use **Displays** to choose the primary display for games or **Keep Windows default**, optionally turn off other displays on launch, rename monitors, and run a short topology trial. Overview follows the configured play primary in real time. Under **General**, configure refresh rate for the play-primary display, HDR policy and metadata names, and the Desktop top panel. Per-game overrides for HDR, refresh rate, and display live in each game's context menu under **Display Manager**; **Keep global settings** leaves that area on the global policy. Saved overrides are listed under **Game Profiles**. Notifications and Maintenance tools (including native HDR migration and RestoreHost lease tests) live under **Notifications** and **Advanced**.

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
```

For custom layouts, Display Manager exposes display and HDR state through:

```xml
{PluginSettings Plugin=DisplayManager, Path=PrimaryDisplayAlias}
```

Useful paths include `PrimaryDisplayName`, `PrimaryDisplayAlias`, `ConnectedDisplayCount`, `HdrPolicyLabel`, `HdrStatusLabel`, `HasHdrMetadata`, `SelectedGameName`, `DisplaysSummary`, and `TopPanelTooltip`. The theme API exposes `ApiVersion` and `Supports*` capability flags (`SupportsDisplayList`, `SupportsHdrStatus`, `SupportsHdrPolicy`, `SupportsHdrMetadata`, `SupportsTopPanel`, `SupportsRefreshRatePolicy`) so themes can conditionally enable integrations. API `1.0.0` is the first stable surface; do not rename properties without bumping `ApiVersion`.

The repository and release package include a fully commented example at [`Examples/FullscreenThemeIntegration.xaml`](Examples/FullscreenThemeIntegration.xaml). Deeper notes live in [`docs/THEME-API.md`](docs/THEME-API.md).

## Localization

The plugin uses Playnite localization resource dictionaries under `Localization/`. Unsupported locales fall back to English. Community translation contributions are welcome.

## Support

If you find this project useful and want to support its development, consider buying me a coffee!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/naerian)
