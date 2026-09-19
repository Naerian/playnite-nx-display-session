# Instalación e inicio rápido

## Instalar

1. Descarga el `.pext` más reciente en [Releases](https://github.com/Naerian/playnite-nx-display-session/releases/latest).
2. En Playnite: **Complementos → Instalar desde archivo** y elige el paquete.
3. Reinicia Playnite si lo pide.
4. Abre **Complementos → Extensiones → Display Manager → Ajustes**.

## Lista de primera puesta en marcha

1. **Pantallas** — confirma TV y secundaria; elige el objetivo por defecto para juegos.
2. **HDR** — elige una política global (ver [HDR](ES-HDR)):
   - **1** No tocar el HDR.
   - **2** Escribir HDR on en objetivos capaces al iniciar un juego.
   - **3** Metadatos: HDR on solo si Features/Tags coinciden (nombres por defecto: `HDR`, `HDR10`, `Dolby Vision`, `Auto HDR`, `HDR10+`).
3. **EnableSystemHdr nativo** — el asistente / Mantenimiento puede limpiar el flag de la biblioteca para que la restauración nativa no pelee con NX. Ver [Checkbox HDR nativo](ES-Checkbox-HDR-nativo).
4. Lanza una sesión corta, sal y confirma que topología y SDR vuelven.

## Override HDR por juego

Menú contextual de la biblioteca → **Display Manager → HDR**: Heredar / Forzar on / Forzar off (SDR) / No tocar. El override siempre gana a la política global.

## Verificar restauración

- Tras salir, el escritorio debe coincidir con el estado previo.
- El HDR debe quedar off cuando NX escribió SDR (no confíes en el readback de Ajustes bajo ACM — ver [Solución ACM](ES-Solucion-de-Problemas-ACM)).
- Log de RestoreHost (si hace falta): `%TEMP%\PlayniteDisplayManager-RestoreHost.log`

## Compilar desde fuente

```powershell
.\package.ps1
```

Requiere SDK `dotnet` y Playnite Toolbox (`C:\Playnite\Toolbox.exe`, o `-ToolboxPath`).
