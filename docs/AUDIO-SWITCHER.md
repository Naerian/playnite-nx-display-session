# Audio Switcher hook

Display Manager can store an optional **associated playback device id** per game and ask **Audio Switcher** to switch to it on game start (and restore the previous device on stop).

## Rules

- Display Manager **never** calls WASAPI / changes Windows audio itself.
- Soft discovery by plugin Guid `708b6ec4-bf96-4c0d-bd9d-fe0aa04d6bf1` (`PlayniteApi.Addons.Plugins`).
- Public reflection only: `GetThemeSelectorDevices`, `GetCurrentDeviceId`, `SetThemeSelectedDevice`.
- If Audio Switcher is missing, the association is kept but ignored at launch.

## Settings

General → Audio Switcher: status + enable hook checkbox.

## Game menu

Display Manager → Audio → device list (or “not installed”).

## Spanish

Ver [AUDIO-SWITCHER.es.md](AUDIO-SWITCHER.es.md).
