# Theme API v1 (Display Manager)

**SourceName:** `DisplayManager`  
**ApiVersion:** `1.0.0` (`Theme.ApiVersion`)

## Elementos personalizados

| x:Name | Control |
|--------|---------|
| `DisplayManager_DisplayList` | Pantallas conectadas (nombre, modo, primaria) |
| `DisplayManager_HdrStatus` | Política HDR global, estado honesto con ACM, metadatos HDR del juego seleccionado |

## PluginSettings (`SettingsRoot` = `Theme`)

Rutas útiles: `PrimaryDisplayName`, `PrimaryDisplayAlias`, `ConnectedDisplayCount`, `HdrPolicyLabel`, `HdrStatusLabel`, `HasHdrMetadata`, `SelectedGameName`, `DisplaysSummary`, `TopPanelTooltip`.

Flags `Supports*`: `SupportsDisplayList`, `SupportsHdrStatus`, `SupportsHdrPolicy`, `SupportsHdrMetadata`, `SupportsTopPanel`, `SupportsRefreshRatePolicy`.

## Top panel (Escritorio)

Botón opcional (Ajustes → General → Mostrar en el panel superior). Abre los ajustes del plugin.

## Ejemplo

Ver [Examples/FullscreenThemeIntegration.xaml](../Examples/FullscreenThemeIntegration.xaml).
