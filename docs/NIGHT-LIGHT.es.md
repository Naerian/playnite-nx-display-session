# Luz nocturna (recortada en v1)

Display Manager **no gestiona** la Luz nocturna de Windows en esta versión. El control se eliminó de Ajustes para que no parezca un interruptor roto.

## Por qué

Windows **no tiene una API pública soportada** de Luz nocturna fiable en Windows 10 y Windows 11. Herramientas de la comunidad (incluido [nightlight-cli](https://github.com/nathanbabcock/nightlight-cli)) reverse-enginean valores binarios no documentados de `CloudStore`. Esos formatos cambian entre builds; el propio proyecto avisa de roturas en actualizaciones recientes de Windows (2026). Una escritura mala puede dejar la Luz nocturna rota hasta cerrar sesión o reiniciar.

Eso incumple la regla de producto del HDR: la restauración debe ser honesta y fiable en HTPC / sofá.

## Qué hacer en su lugar

Cambia la Luz nocturna en **Ajustes de Windows → Sistema → Pantalla → Luz nocturna**.

## English

See [NIGHT-LIGHT.md](NIGHT-LIGHT.md).
