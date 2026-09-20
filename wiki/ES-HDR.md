# HDR

Display Manager **posee el HDR** cuando está instalado. Planifica una escritura on/off para la sesión y restaura escribiendo HDR **off** — no confía en el GET de Windows bajo Automatic Color Management.

Detalle técnico: [docs/HDR-Y-ACM.es.md](https://github.com/Naerian/playnite-nx-display-session/blob/main/docs/HDR-Y-ACM.es.md).

## Políticas globales

| Política | Comportamiento |
|----------|----------------|
| **1 — No tocar** | Sin escritura HDR al inicio; sí restaura topología. |
| **2 — Siempre on** | Escribe HDR on en objetivos activos capaces al iniciar el juego. |
| **3 — Metadatos** | Escribe HDR on solo si Features (y opcionalmente Tags) locales coinciden con nombres configurados. Sin red en el lanzamiento. |

Nombres por defecto: `HDR`, `HDR10`, `Dolby Vision`, `Auto HDR`, `HDR10+`.

## Override por juego

Menú contextual → Display Manager → HDR:

- **Mantener configuración global** — usa la política HDR global.
- **Forzar on** — escribe HDR on.
- **Forzar off (SDR)** — escribe HDR off.
- **No tocar** — omite escrituras HDR en este juego.

El override siempre gana a la política global.

## Flujo de sesión

1. Resolver plan efectivo (global + override + metadatos).
2. Capturar snapshot de topología; armar lease de RestoreHost.
3. **Escribir** HDR on u off según el plan (APIs CCD de color avanzado / estado HDR).
4. Al parar / cancelar / salir de Playnite: restaurar topología y **escribir HDR off** en esos objetivos.

El snapshot guarda la intención de escritura (`enable: false` al restaurar). No pregunta a Windows qué “es” el HDR.

## Honestidad en Overview

Cuando ACM puede aplicar, Overview muestra HDR como **Unknown**. No es un bug — el readback miente. Usa **Win+Alt+B** o Ajustes → HDR → **Escribir HDR off ahora** para forzar SDR.

## Relacionado

- [Solución de problemas ACM](ES-Solucion-de-Problemas-ACM)
- [Checkbox HDR nativo](ES-Checkbox-HDR-nativo)
