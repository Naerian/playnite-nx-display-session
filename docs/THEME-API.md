# Theme API v1 (Display Manager)

**SourceName:** `DisplayManager`  
**ApiVersion:** `1.0.0` (`Theme.ApiVersion`)

## Custom elements

| x:Name | Control |
|--------|---------|
| `DisplayManager_DisplayList` | Connected displays (name, mode, primary) |
| `DisplayManager_HdrStatus` | Global HDR policy, ACM-honest status, selected-game HDR metadata |

## PluginSettings (`SettingsRoot` = `Theme`)

Useful paths: `PrimaryDisplayName`, `PrimaryDisplayAlias`, `ConnectedDisplayCount`, `HdrPolicyLabel`, `HdrStatusLabel`, `HasHdrMetadata`, `SelectedGameName`, `DisplaysSummary`, `TopPanelTooltip`.

`Supports*` flags: `SupportsDisplayList`, `SupportsHdrStatus`, `SupportsHdrPolicy`, `SupportsHdrMetadata`, `SupportsTopPanel`, `SupportsRefreshRatePolicy`.

## Desktop top panel

Optional button (Settings → General → Show in Desktop top panel). Opens plugin settings. Shows primary display alias + HDR policy tooltip.

## Example

See [Examples/FullscreenThemeIntegration.xaml](../Examples/FullscreenThemeIntegration.xaml).

## Spanish

Ver [THEME-API.es.md](THEME-API.es.md).
