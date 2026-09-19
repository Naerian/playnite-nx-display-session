# Pantallas

Display Manager enumera las pantallas activas de Windows y aplica una topología de sesión para que el juego use la pantalla que te importa (típicamente la TV en un HTPC de sofá).

## Identidad

Las pantallas se identifican sobre todo por **EDID** e info de ruta CCD, no solo por rutas de instancia frágiles. Cuando Windows expone datos suficientes, la identidad aguanta mejor cambios de cable o GPU que el matching solo por nombre.

Si un dock, splitter o adaptador recorta el EDID, el matching puede caer a claves más débiles — Overview y Ajustes → Pantallas muestran lo resuelto.

## Pantalla objetivo

En Ajustes → Pantallas (y controles relacionados de Overview):

- Elige qué pantalla activa es el **objetivo del juego**.
- Confirma que las secundarias quedan como esperas tras apply/restore.
- Tras cambiar el objetivo, valida con un ciclo corto de lanzar/salir.

## Frecuencia de refresco

Cuando está configurado, Display Manager puede cambiar el refresh en la ruta de sesión (`ChangeDisplaySettingsEx`). La restauración vuelve al modo anterior con el snapshot / lease de RestoreHost.

## Qué cubre la restauración

- Topología activa (rutas habilitadas / primaria según el snapshot).
- Escritura HDR off en objetivos de sesión (ver [HDR](ES-HDR)).
- Refresh rate si se cambió en la sesión.

## Qué no cubre (v1)

- Mover la propia ventana Fullscreen de Playnite entre monitores.
- Resoluciones personalizadas por juego más allá de la topología aplicada.
- Upscalers, VRR, CEC.

## Diagnóstico

- Overview lista pantallas resueltas y el plan HDR efectivo del juego seleccionado.
- Log de RestoreHost: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
- Si la restauración parece colgada, comprueba que RestoreHost no esté retenido por un lease roto (log + Administrador de tareas).
