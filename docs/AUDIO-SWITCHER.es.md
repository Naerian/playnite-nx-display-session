# Hook Audio Switcher

Display Manager puede guardar un **id de dispositivo de reproducción** opcional por juego y pedir a **Audio Switcher** que lo active al iniciar (y restaure el anterior al salir).

## Reglas

- Display Manager **nunca** llama a WASAPI ni cambia el audio de Windows por su cuenta.
- Descubrimiento suave por Guid `708b6ec4-bf96-4c0d-bd9d-fe0aa04d6bf1`.
- Solo reflexión pública: `GetThemeSelectorDevices`, `GetCurrentDeviceId`, `SetThemeSelectedDevice`.
- Si Audio Switcher no está, la asociación se guarda pero se ignora al lanzar.

## Ajustes

General → Audio Switcher: estado + checkbox del hook.

## Menú del juego

Display Manager → Audio → lista de dispositivos (o «no instalado»).
