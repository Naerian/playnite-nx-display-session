# HDR y Administración automática del color (ACM)

Display Manager **es dueño del HDR** cuando está instalado. No confía en la lectura del estado HDR de Windows con Administración automática del color.

## Por qué miente la lectura

En Windows 11 (sobre todo 24H2) con **Administrar el color de las aplicaciones automáticamente** (ACM), `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` a menudo no coincide con lo que ves. El `EnableSystemHdr` nativo de Playnite puede dejar el HDR encendido porque el restore se fiaba de esa lectura.

## Qué hace Display Manager

1. **Al iniciar un juego**: resuelve el plan (política global + override por juego + Features/Tags opcionales), captura snapshot, arma RestoreHost y **escribe** HDR on u off según el plan.
2. **Al salir / cancelar / cerrar Playnite**: **escribe HDR off** en esos destinos (y restaura topología). El snapshot guarda la intención de escritura (`enable: false`) — no pregunta a Windows «cómo está» el HDR.
3. El Resumen muestra HDR como **Desconocido** si ACM puede interferir. Es honestidad deliberada.
4. **Política 3 (metadatos)**: empareja `Game.Features` locales (y opcionalmente `Tags`) con nombres configurables — por defecto `HDR`, `HDR10`, `Dolby Vision`, `Auto HDR`, `HDR10+`. Sin red en el lanzamiento.
5. **Override por juego** (menú contextual → Display Manager → HDR): Heredar / Forzar on / Forzar off (SDR) / No tocar. El override siempre gana.
6. **EnableSystemHdr nativo**: el asistente (y Mantenimiento) puede limpiar el flag en toda la biblioteca (con backup reversible). En cada lanzamiento, si el flag vuelve a estar on, NX lo limpia y notifica una vez — para no apilar restores. El checkbox nativo puede seguir visible; no hay API pública del SDK para ocultarlo.

## Diagnóstico

- Comprobación visual: **Win+Alt+B**.
- Ajustes → HDR → **Escribir HDR off ahora**.
- Resumen → **Juego seleccionado (HDR)** previsualiza metadatos y el plan efectivo.
- Resumen / Mantenimiento → conteo de `EnableSystemHdr` y limpiar/restaurar.
- Log RestoreHost: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
