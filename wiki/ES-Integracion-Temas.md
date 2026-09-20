# Integracion con temas

Display Manager expone controles Fullscreen pensados para mando mediante elementos personalizados de Playnite. Usa nombres `ContentControl` con SourceName `DisplayManager`.

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

Origen de ajustes:

```xml
{PluginSettings Plugin=DisplayManager, Path=Theme.ApiVersion}
```

Display Manager registra `SourceName` **DisplayManager**, `SettingsRoot` **Theme** y Theme API `ApiVersion` **1.1.0**.

Rutas utiles: `Theme.PrimaryDisplayAlias`, `Theme.DisplaysSummary`, `Theme.HdrPolicyLabel`, `Theme.ActiveDisplayProfileName`, `Theme.ActiveProfileSourceLabel`, `Theme.SessionActive`, `Theme.SessionGameName` y `Theme.OpenSettingsCommand`.

Consulta [EN-Theme-API-Reference](EN-Theme-API-Reference) y el ejemplo incluido en `Examples/FullscreenThemeIntegration.xaml`.
