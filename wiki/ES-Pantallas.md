# Pantallas

Display Manager separa el **inventario de pantallas conectadas** de los **perfiles de pantalla**.

Ajustes -> Pantallas es solo para pantallas conectadas: identidad resuelta, nombres personalizados, visibilidad en Display Manager e Identificar. El comportamiento de perfiles vive en Ajustes -> General.

## Perfiles de pantalla

En Ajustes -> General -> Perfiles de pantalla, cada perfil puede definir:

- Pantalla principal para juegos, o mantener la predeterminada de Windows.
- Si se apagan otras pantallas al iniciar un juego.
- Politica de pantalla ausente y pantalla de respaldo.
- Valores de HDR, frecuencia y resolucion.

Los valores incluidos son **Solo TV** para una sesion solo en TV y **PC / Desktop** para el escritorio normal. Un perfil es el **predeterminado al lanzar** salvo que un perfil por juego o plataforma lo sobrescriba.

## Pantallas ausentes

Ajustes -> General -> Pantalla ausente decide que ocurre si la pantalla preferida del perfil seleccionado no esta conectada: usar primaria de Windows, usar pantalla de respaldo, o avisar y continuar.

## Resolucion y frecuencia

Resolucion para juegos se aplica tras el perfil de pantalla y antes de frecuencia/HDR. Las frecuencias son las que Windows reporta para la pantalla principal de juegos a la resolucion actual.

El **retardo de asentamiento** en General -> Opciones espera tras cambios de distribucion, resolucion, frecuencia o HDR antes de continuar con el juego. Usalo para pantallas o AVRs que necesiten mas tiempo.

## Prioridad

1. Perfil por juego desde el menu contextual.
2. Perfil por plataforma desde Ajustes -> General -> Perfiles por plataforma.
3. Perfil de pantalla predeterminado y ajustes globales.

## Restauracion

La restauracion devuelve la distribucion previa, cambios de resolucion/frecuencia hechos para la sesion y escritura HDR off en los objetivos. RestoreHost mantiene esta proteccion si Playnite se cierra de forma inesperada.
