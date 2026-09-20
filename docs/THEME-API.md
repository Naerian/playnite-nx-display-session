# Theme API v1.1 (Display Manager)

**SourceName:** `DisplayManager`  
**PluginSettings SettingsRoot:** `Theme`  
**ApiVersion:** `1.1.0` (`Theme.ApiVersion`)

## Custom elements

| x:Name | Control |
| --- | --- |
| `DisplayManager_DisplayList` | Connected displays (name, mode, primary). |
| `DisplayManager_HdrStatus` | Global HDR policy, ACM-honest status, selected-game HDR metadata. |
| `DisplayManager_ActiveProfile` | Active/default display profile summary. |
| `DisplayManager_SessionStatus` | Current Display Manager session status. |
| `DisplayManager_DisplaysSummary` | Compact connected display summary. |
| `DisplayManager_OpenSettingsButton` | Opens Display Manager settings. |

## PluginSettings

Use `Plugin=DisplayManager` and `Path=Theme.<Property>`.

Useful paths: `PrimaryDisplayName`, `PrimaryDisplayAlias`, `ConnectedDisplayCount`, `HdrPolicyLabel`, `HdrStatusLabel`, `HasHdrMetadata`, `SelectedGameName`, `DisplaysSummary`, `TopPanelTooltip`, `ActiveDisplayProfileName`, `ActiveProfileSourceLabel`, `SessionActive`, `SessionGameName`, and `OpenSettingsCommand`.

`Supports*` flags: `SupportsDisplayList`, `SupportsHdrStatus`, `SupportsHdrPolicy`, `SupportsHdrMetadata`, `SupportsTopPanel`, `SupportsRefreshRatePolicy`, `SupportsActiveProfile`, `SupportsSessionStatus`, `SupportsDisplaysSummary`, `SupportsOpenSettings`.

## Example

See [Examples/FullscreenThemeIntegration.xaml](../Examples/FullscreenThemeIntegration.xaml).
