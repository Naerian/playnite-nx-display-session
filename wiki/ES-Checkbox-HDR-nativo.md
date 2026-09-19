# Por qué queda el checkbox HDR nativo

Playnite Desktop (y algunos flujos Fullscreen) sigue mostrando la opción nativa **Enable HDR** / `EnableSystemHdr`. Display Manager no puede ocultar ese checkbox: **no hay API pública del SDK de Playnite** para quitar o sustituir controles nativos del detalle del juego.

## Qué hace NX en su lugar

1. **Asistente / Mantenimiento** puede limpiar `EnableSystemHdr` en toda la biblioteca (con backup para restauración inversa).
2. En cada lanzamiento, si el flag vuelve a estar on, NX lo limpia y notifica una vez — así la restauración nativa y la de NX no se apilan.
3. El HDR de la sesión lo poseen las políticas de Display Manager y los overrides por juego ([HDR](ES-HDR)).

## Qué debes usar

| Control | Uso |
|---------|-----|
| Política global HDR de Display Manager | Por defecto de la biblioteca |
| Menú contextual → Display Manager → HDR | Por juego: Heredar / Forzar on / Forzar off / No tocar |
| Checkbox nativo Enable HDR | Preferible dejarlo limpio; NX lo limpiará si reaparece |

## FAQ

**¿Desaparecerá el checkbox algún día?**  
Solo si Playnite añade un hook de SDK o UI. Hasta entonces, NX documenta y desarma el flag en lugar de fingir que el control no está.

**Activé el HDR nativo por error.**  
Límpialo en Mantenimiento, o lanza una vez para que NX limpie y notifique; luego pon el override/política NX que quieras.

**¿NX pelea con la restauración nativa?**  
Ese era el fallo con ACM + GET de confianza. NX escribe SDR al salir y limpia el flag nativo para que ambas rutas no dejen el HDR pegado en on.
