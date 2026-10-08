# Descripcion general

Display Manager es un GenericPlugin de Playnite que gestiona **distribucion de pantallas, resolucion, frecuencia y HDR** de Windows para sesiones de juego. Al iniciar un juego aplica los ajustes resueltos; al parar, cancelar o cerrar Playnite restaura el escritorio anterior mediante un lease durable de RestoreHost.

Pensado para jugar en el salon: un PC conectado a la TV, o equipos que alternan entre la TV y otra pantalla.

## Que hace

- Enumera pantallas activas con identidad basada en EDID cuando Windows expone datos suficientes.
- Permite elegir **pantallas principales distintas para Desktop y Fullscreen**, y editar los ajustes de lanzamiento de cada modo (HDR, frecuencia, resolucion, apagado de otras, pantalla ausente). Los perfiles **Solo TV** / **PC / Desktop** siguen para overrides. Al entrar en **pantalla completa** aplica la principal Fullscreen y la mantiene durante la sesion (se restaura al volver al escritorio).
- Gestiona pantallas ausentes con la principal de Windows, pantalla de respaldo, o avisar y continuar.
- Gestiona **HDR** con politica global, valores por perfil de pantalla y overrides por juego/plataforma.
- Opcionalmente aplica **resolucion** y **frecuencia** tras el perfil de pantalla, y espera el **retardo de asentamiento** configurado antes de continuar.
- Expone controles para temas Fullscreen y `PluginSettings` con SourceName `DisplayManager`.

## Prioridad

Display Manager resuelve ajustes en este orden: perfil por juego, perfil por plataforma y luego los ajustes de lanzamiento del modo actual de Playnite (Desktop o Fullscreen). `Mantener configuracion global` en un menu de juego elimina ese override para que aplique la capa siguiente.

## Prioridades de diseno

Estabilidad y honestidad por encima de UI inteligente. Bajo Automatic Color Management (ACM), el readback de HDR de Windows no es fiable, asi que Display Manager escribe el estado pretendido y puede mostrar HDR como **Unknown**.

La luz nocturna no se gestiona en v1: no hay API publica soportada Win10+Win11 fiable en ambas lineas de SO.

## Fuera de alcance (v1)

Upscalers, VRR, CEC, inyeccion en procesos, mandos Harmony y cambio de dispositivo de audio.

Continua con [Instalacion e inicio rapido](ES-Instalacion-e-Inicio-Rapido).
