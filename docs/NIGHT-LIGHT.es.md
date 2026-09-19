# Luz nocturna (experimental — recortada en v1)

Display Manager **no gestiona** la Luz nocturna de Windows en esta versión.

## Por qué

Windows **no ofrece una API pública y soportada** de Luz nocturna que sea determinista en Windows 10 y Windows 11. Las herramientas de la comunidad reverse-enginean valores binarios no documentados de `CloudStore`. Esos formatos cambian entre builds; una escritura mala puede dejar la Luz nocturna rota hasta cerrar sesión o reiniciar.

Eso incumple la regla de producto del HDR: el restore debe ser honesto y fiable en HTPC / sofá. Inventar blobs de CloudStore mentiría en el Resumen y podría romper el escritorio.

## Qué ves en Ajustes

- **General → Luz nocturna**: solo «No tocar», marcada como experimental / recortada.
- **Resumen**: «No gestionada — no hay API soportada Win10+Win11».

## Qué hacer en su lugar

Cámbiala en **Configuración de Windows → Sistema → Pantalla → Luz nocturna**.
