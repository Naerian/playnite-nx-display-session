# Pantallas

Display Manager enumera las pantallas activas de Windows y aplica una topología de sesión para que el juego use la pantalla que te importa (típicamente la TV en un HTPC de sofá).

## Identidad

Las pantallas se identifican sobre todo por **EDID** e info de ruta CCD, no solo por rutas de instancia frágiles. Cuando Windows expone datos suficientes, la identidad aguanta mejor cambios de cable o GPU que el matching solo por nombre.

Si un dock, splitter o adaptador recorta el EDID, el matching puede caer a claves más débiles — Overview y Ajustes → Pantallas muestran lo resuelto.

## Pantalla principal para juegos

En Ajustes → Pantallas:

- Elige la **pantalla principal para juegos**, o **Mantener la predeterminada de Windows** para no tocar la primaria del sistema.
- Opcionalmente **apaga otras pantallas** al lanzar un juego (siempre se confirma en la prueba).
- Renombra pantallas para Playnite, usa **Identificar** para ver una etiqueta en cada monitor y previsualiza la topología con los botones de prueba.

Overview y las cards de pantallas siguen en tiempo real la primaria configurada para juegos.

## Override de pantalla por juego

Menú contextual → Display Manager → Pantalla:

- **Mantener configuración global** — usa Ajustes → Pantallas.
- **Mantener la predeterminada de Windows** — deja la primaria de Windows en este juego.
- Una pantalla conectada concreta — esa pantalla será la primaria solo para ese juego.

## Frecuencia de refresco

Cuando está configurado en Ajustes → General → Frecuencia de refresco, Display Manager puede cambiar el refresh en la ruta de la pantalla principal para juegos (`ChangeDisplaySettingsEx`). Solo se listan las tasas que reporta esa pantalla a su resolución actual. La restauración vuelve al modo anterior con el snapshot / lease de RestoreHost.

Override por juego: menú contextual → Display Manager → Frecuencia → **Mantener configuración global** o una política/tasa concreta.

## Qué cubre la restauración

- Topología activa (rutas habilitadas / primaria según el snapshot).
- Escritura HDR off en objetivos de sesión (ver [HDR](ES-HDR)).
- Refresh rate si se cambió en la sesión.

## Qué no cubre (v1)

- Mover la propia ventana Fullscreen de Playnite entre monitores.
- Resoluciones personalizadas por juego más allá de la topología aplicada.
- Upscalers, VRR, CEC.
- Cambio de dispositivo de audio (usa [Audio Switcher](https://github.com/Naerian/playnite-nx-audio-switcher) si lo necesitas).

## Diagnóstico

- Overview lista pantallas resueltas y políticas de sesión.
- Log de RestoreHost: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
- Si la restauración parece colgada, comprueba que RestoreHost no esté retenido por un lease roto (log + Administrador de tareas).
