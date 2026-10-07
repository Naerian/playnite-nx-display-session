# Pantallas

Display Manager separa el **inventario de pantallas conectadas** de los **perfiles de pantalla**.

Ajustes -> Pantallas es solo para pantallas conectadas: identidad resuelta, nombres personalizados, visibilidad en Display Manager, Identificar y la opcion de layout en pantalla completa. El comportamiento al lanzar (HDR, frecuencia, resolucion, pantalla ausente) vive en Ajustes -> Al lanzar un juego. Los perfiles por juego y plataforma viven en Ajustes -> General.

## Perfiles de pantalla

En Ajustes -> General -> Perfiles de pantalla, cada perfil puede definir:

- Pantalla principal para juegos, o mantener la predeterminada de Windows.
- Si se apagan otras pantallas al iniciar un juego.
- Politica de pantalla ausente y pantalla de respaldo.
- Valores de HDR, frecuencia y resolucion.

Los valores incluidos son **Solo TV** para una sesion solo en TV y **PC / Desktop** para el escritorio normal. Un perfil es el **predeterminado al lanzar** salvo que un perfil por juego o plataforma lo sobrescriba.

## Layout en pantalla completa

En Pantallas, **Aplicar la distribucion de pantallas al entrar en pantalla completa** esta desactivado por defecto. Si lo activas, Playnite en pantalla completa usa la misma pantalla principal, apagado de otras y respaldo que al lanzar un juego. El layout se mantiene durante toda la sesion de pantalla completa para que los juegos no cambien de monitor entre uno y otro. Al volver al escritorio se restaura el layout anterior.

Uso tipico en el salon: TV como principal para juegos, apagar las demas, monitor del PC como respaldo si la TV esta apagada o desconectada.

## Menu contextual del juego

Menu contextual de la biblioteca → **Display Manager**:

- **Pantalla** — heredar, mantener Windows, o elegir una pantalla conectada. Aqui aparecen los nombres personalizados de Ajustes.
- **Otras pantallas** — heredar, apagar otras pantallas, o dejarlas encendidas para ese juego.

## Pantallas ausentes

Ajustes -> Al lanzar un juego -> Pantalla ausente decide que ocurre si la pantalla preferida del perfil seleccionado no esta conectada: usar primaria de Windows, usar pantalla de respaldo, o avisar y continuar.

## Resolucion y frecuencia

Resolucion para juegos se aplica tras el perfil de pantalla y antes de frecuencia/HDR. Las frecuencias son las que Windows reporta para la pantalla principal de juegos a la resolucion actual.

El **retardo de asentamiento** en General -> Opciones espera tras cambios de distribucion, resolucion, frecuencia o HDR antes de continuar con el juego. Usalo para pantallas o AVRs que necesiten mas tiempo.

## Prioridad

1. Perfil por juego desde el menu contextual.
2. Perfil por plataforma desde Ajustes -> General -> Perfiles por plataforma.
3. Perfil de pantalla predeterminado y ajustes globales.

## Restauracion

La restauracion devuelve la distribucion previa, cambios de resolucion/frecuencia hechos para la sesion y escritura HDR off en los objetivos. RestoreHost mantiene esta proteccion si Playnite se cierra de forma inesperada.
