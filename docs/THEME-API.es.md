# Theme API v1.1 (Display Manager)

**SourceName:** `DisplayManager`  
**PluginSettings SettingsRoot:** `Theme`  
**ApiVersion:** `1.1.0` (`Theme.ApiVersion`)

## Elementos personalizados

| x:Name | Control |
| --- | --- |
| `DisplayManager_DisplayList` | Pantallas conectadas (nombre, modo, primaria). |
| `DisplayManager_HdrStatus` | Politica HDR global, estado honesto con ACM y metadatos HDR del juego seleccionado. |
| `DisplayManager_ActiveProfile` | Resumen del perfil de pantalla activo/predeterminado. |
| `DisplayManager_SessionStatus` | Estado de la sesion de Display Manager. |
| `DisplayManager_DisplaysSummary` | Resumen compacto de pantallas. |
| `DisplayManager_OpenSettingsButton` | Abre los ajustes de Display Manager. |

## PluginSettings

Usa `Plugin=DisplayManager` y `Path=Theme.<Propiedad>`.

Rutas utiles: `PrimaryDisplayName`, `PrimaryDisplayAlias`, `ConnectedDisplayCount`, `HdrPolicyLabel`, `HdrStatusLabel`, `HasHdrMetadata`, `SelectedGameName`, `DisplaysSummary`, `TopPanelTooltip`, `ActiveDisplayProfileName`, `ActiveProfileSourceLabel`, `SessionActive`, `SessionGameName` y `OpenSettingsCommand`.

Flags `Supports*`: `SupportsDisplayList`, `SupportsHdrStatus`, `SupportsHdrPolicy`, `SupportsHdrMetadata`, `SupportsTopPanel`, `SupportsRefreshRatePolicy`, `SupportsActiveProfile`, `SupportsSessionStatus`, `SupportsDisplaysSummary`, `SupportsOpenSettings`.

## Ejemplo

Ver [Examples/FullscreenThemeIntegration.xaml](../Examples/FullscreenThemeIntegration.xaml).
