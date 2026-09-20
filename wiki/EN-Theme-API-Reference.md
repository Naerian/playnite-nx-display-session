# Theme API Reference

**SourceName:** `DisplayManager`  
**PluginSettings SettingsRoot:** `Theme`  
**ApiVersion:** `1.1.0`

## ContentControls

| Name | Purpose |
| --- | --- |
| `DisplayManager_DisplayList` | Connected display list. |
| `DisplayManager_HdrStatus` | HDR policy/status summary. |
| `DisplayManager_ActiveProfile` | Active/default display profile summary. |
| `DisplayManager_SessionStatus` | Current session status. |
| `DisplayManager_DisplaysSummary` | Compact display summary. |
| `DisplayManager_OpenSettingsButton` | Button that opens Display Manager settings. |

## PluginSettings paths

Use paths with the `Theme.` prefix, for example:

```xml
<TextBlock Text="{PluginSettings Plugin=DisplayManager, Path=Theme.DisplaysSummary}" />
```

Common paths: `ApiVersion`, `PrimaryDisplayName`, `PrimaryDisplayAlias`, `ConnectedDisplayCount`, `HdrPolicyLabel`, `HdrStatusLabel`, `HasHdrMetadata`, `SelectedGameName`, `DisplaysSummary`, `TopPanelTooltip`, `ActiveDisplayProfileName`, `ActiveProfileSourceLabel`, `SessionActive`, `SessionGameName`, and `OpenSettingsCommand`.

Capability flags: `SupportsDisplayList`, `SupportsHdrStatus`, `SupportsHdrPolicy`, `SupportsHdrMetadata`, `SupportsTopPanel`, `SupportsRefreshRatePolicy`, `SupportsActiveProfile`, `SupportsSessionStatus`, `SupportsDisplaysSummary`, and `SupportsOpenSettings`.
