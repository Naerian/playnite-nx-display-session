# Descripcion general

Display Manager es un GenericPlugin de Playnite que gestiona **perfiles de pantalla, HDR, resolucion y frecuencia** para sesiones de juego. Al iniciar un juego aplica el perfil resuelto; al parar, cancelar o cerrar Playnite restaura el escritorio anterior mediante un lease durable de RestoreHost.

## Que hace

- Enumera pantallas activas con identidad basada en EDID cuando Windows expone datos suficientes.
- Permite elegir la **pantalla principal para juegos** mediante **perfiles de pantalla**. Los valores incluidos son **Solo TV** para jugar en sofa/TV y **PC / Desktop** para mantener el escritorio normal.
- Gestiona pantallas ausentes con primaria de Windows, pantalla de respaldo, o avisar y continuar.
- Gestiona **HDR** con politica global, valores por perfil de pantalla y overrides por juego/plataforma.
- Opcionalmente aplica **resolucion** y **frecuencia** tras el perfil de pantalla, y espera el **retardo de asentamiento** configurado antes de continuar.
- Expone controles para temas Fullscreen y `PluginSettings` con SourceName `DisplayManager`.

## Prioridad

Display Manager resuelve ajustes en este orden: perfil por juego, perfil por plataforma, perfil de pantalla predeterminado y ajustes globales. `Mantener configuracion global` en un menu de juego elimina ese override para que aplique la capa siguiente.

## Prioridades de diseno

Estabilidad y honestidad por encima de UI inteligente. Bajo Automatic Color Management (ACM), el readback de HDR de Windows no es fiable, asi que Display Manager escribe el estado pretendido y puede mostrar HDR como **Unknown**.

La luz nocturna no se gestiona en v1: no hay API publica soportada Win10+Win11 fiable en ambas lineas de SO.

## Fuera de alcance (v1)

Upscalers, VRR, CEC, inyeccion en procesos, mandos Harmony y cambio de dispositivo de audio.

Continua con [Instalacion e inicio rapido](ES-Instalacion-e-Inicio-Rapido).
