# Solución de problemas ACM

En Windows 11 (sobre todo 24H2) con **Administrar automáticamente los colores de las aplicaciones** (ACM), `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` suele reportar un estado que no coincide con lo que ves. El `EnableSystemHdr` nativo de Playnite puede dejar el HDR encendido tras un juego porque la restauración confiaba en esa lectura.

Display Manager **nunca** confía en ese GET para restaurar.

## Síntomas

- Ajustes de Windows dice que el HDR está off, pero la TV sigue en HDR (o al revés).
- Tras un juego, el escritorio queda en HDR aunque Playnite “restauró”.
- Overview muestra HDR como **Unknown**.

## Qué hacer

1. Prefiere **confirmación visual**: **Win+Alt+B** (toggle HDR de Xbox Game Bar) y mira la pantalla.
2. En Ajustes de Display Manager → HDR, usa **Escribir HDR off ahora** para forzar SDR por escritura en pantallas activas capaces.
3. Limpia `EnableSystemHdr` nativo vía Overview / Mantenimiento para que Playnite y NX no apilen restauraciones ([Checkbox HDR nativo](ES-Checkbox-HDR-nativo)).
4. Revisa el log de RestoreHost: `%TEMP%\PlayniteDisplayManager-RestoreHost.log`
5. Si ACM es opcional en tu flujo, prueba a desactivar **Administrar automáticamente los colores…** en color de Windows y retestea — NX sigue escribiendo; ACM envenena sobre todo el *readback*.

## Por qué Unknown es correcto

Mostrar On/Off con confianza desde una API mentirosa sería peor que Unknown. El **plan** efectivo del juego seleccionado sigue previsualizándose en Overview (match de metadatos + override).

## Relacionado

- [HDR](ES-HDR)
- Notas técnicas: [HDR-Y-ACM.es.md](https://github.com/Naerian/playnite-nx-display-session/blob/main/docs/HDR-Y-ACM.es.md)
