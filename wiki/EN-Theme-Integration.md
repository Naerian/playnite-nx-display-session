# Theme Integration

Display Manager exposes controller-friendly Fullscreen controls through Playnite custom elements. Use `ContentControl` names with SourceName `DisplayManager`.

## ContentControls

```xml
<ContentControl x:Name="DisplayManager_DisplayList" />
<ContentControl x:Name="DisplayManager_HdrStatus" />
<ContentControl x:Name="DisplayManager_ActiveProfile" />
<ContentControl x:Name="DisplayManager_SessionStatus" />
<ContentControl x:Name="DisplayManager_DisplaysSummary" />
<ContentControl x:Name="DisplayManager_OpenSettingsButton" />
```

## PluginSettings

Settings source:

```xml
{PluginSettings Plugin=DisplayManager, Path=Theme.ApiVersion}
```

Display Manager registers `SourceName` **DisplayManager**, `SettingsRoot` **Theme**, and Theme API `ApiVersion` **1.1.0**.

Useful paths include `Theme.PrimaryDisplayAlias`, `Theme.DisplaysSummary`, `Theme.HdrPolicyLabel`, `Theme.ActiveDisplayProfileName`, `Theme.ActiveProfileSourceLabel`, `Theme.SessionActive`, `Theme.SessionGameName`, and `Theme.OpenSettingsCommand`.

See [EN-Theme-API-Reference](EN-Theme-API-Reference) and the packaged example at `Examples/FullscreenThemeIntegration.xaml`.
