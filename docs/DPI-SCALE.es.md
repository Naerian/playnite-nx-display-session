# Escala de pantalla / DPI (Fullscreen)

## Decisión (sí, experimental)

Se publica como ajuste **opt-in** en Avanzado, solo para la sesión de modo Fullscreen de Playnite:

**Forzar escala de pantalla al 100% al entrar en Fullscreen**

Por defecto: **desactivado**.

## Por qué

Las TVs de salón suelen usar escala de Windows al 125% o 150%. Playnite Fullscreen puede verse mal o comportarse raro con esa escala. El contrato es el mismo que con la topología: aplicar al entrar y restaurar al salir (o si Playnite cae).

## Cómo funciona

Usa paquetes CCD no documentados (`DisplayConfigGet/SetDeviceInfo` tipos -3 / -4). El porcentaje anterior se guarda en el snapshot y RestoreHost también lo restaura.

## Riesgos

Misma clase que Night Light: API no oficial. Dejar el ajuste apagado si no lo necesitas.

## Pruebas manuales

- TV sola a 150% → Fullscreen → 100% → salir → 150%
- TV + monitor → solo cambia el primary de Fullscreen
- Matar Playnite en Fullscreen con la opción activa → RestoreHost restaura la escala
- Opción apagada → sin cambios de escala
