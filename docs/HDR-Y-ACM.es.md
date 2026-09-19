# HDR y Administración automática del color (ACM)

Display Manager **es dueño del HDR** cuando está instalado. No confía en la lectura del estado HDR de Windows con Administración automática del color.

## Por qué miente la lectura

En Windows 11 (sobre todo 24H2) con **Administrar el color de las aplicaciones automáticamente** (ACM), `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` a menudo no coincide con lo que ves. El `EnableSystemHdr` nativo de Playnite puede dejar el HDR encendido porque el restore se fiaba de esa lectura.

## Qué hace Display Manager

1. **Al iniciar un juego** (política global «cualquier juego»): captura snapshot de topología, arma RestoreHost y **escribe** color avanzado / HDR **on** en la primaria si es compatible.
2. **Al salir / cancelar / cerrar Playnite**: **escribe HDR off** en esos destinos (y restaura topología). El snapshot guarda la intención de escritura (`enable: false`) — no pregunta a Windows «cómo está» el HDR.
3. El Resumen muestra HDR como **Desconocido** si ACM puede interferir. Es honestidad deliberada.

## Diagnóstico

- Comprobación visual: **Win+Alt+B**.
- Ajustes → HDR → **Escribir HDR off ahora**.
- Log RestoreHost: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
