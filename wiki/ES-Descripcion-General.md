# Descripción general

Display Manager es un GenericPlugin de Playnite que posee la **topología de pantallas y el HDR de Windows** en las sesiones de juego. Al iniciar un juego aplica tu perfil; al parar, cancelar o cerrar Playnite restaura el escritorio anterior mediante un lease durable de RestoreHost.

## Qué hace

- Enumera pantallas activas con identidad basada en EDID (estable ante cambios de cable/ruta GPU cuando Windows expone datos suficientes).
- Permite elegir la **pantalla principal para juegos** con perfiles de topología (o mantener la de Windows), con fallback si falta.
- Posee el **HDR** con tres políticas globales, overrides por juego y por plataforma, y coincidencia opcional por Features/Tags.
- Opcionalmente reubica Playnite Fullscreen en el monitor primario restaurado tras la sesión.
- Cambia la **frecuencia de refresco** cuando está configurado (ruta ChangeDisplaySettingsEx).
- Arma **RestoreHost** para que la restauración sobreviva a un cierre brusco de Playnite.
- Expone **Theme API** (`SourceName` `DisplayManager`) y un acceso en el panel superior de Desktop.

## Prioridades de diseño

Estabilidad y honestidad por encima de UI “inteligente”. Bajo Automatic Color Management (ACM), el *readback* de HDR de Windows no es fiable — Display Manager **escribe** el estado pretendido y restaura escribiendo SDR. Overview puede mostrar HDR como **Unknown**; es intencional.

La **luz nocturna** no se gestiona en v1: no hay API pública soportada Win10+Win11 fiable en ambas líneas de SO.

## Fuera de alcance (v1)

Resolución por juego más allá del apply de topología actual, upscalers, VRR, mover la ventana Fullscreen de Playnite, CEC, inyección en procesos, Harmony.

## Limitaciones importantes

- Bajo ACM no se confía en el GET de HDR; usa comprobación visual (p. ej. Win+Alt+B).
- La identidad de pantalla depende de EDID / CCD; algunos docks renombran rutas.

Continúa con [Instalación e inicio rápido](ES-Instalacion-e-Inicio-Rapido).
