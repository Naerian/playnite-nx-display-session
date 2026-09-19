using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.NightLight;
using PlayniteDisplayManager.Refresh;

namespace PlayniteDisplayManager
{
    public enum DesktopTopPanelDisplayMode
    {
        IconAndText = 0,
        Icon = 1,
        Text = 2
    }

    public sealed class DisplayManagerSettings : ObservableObject, ISettings
    {
        private readonly PlayniteDisplayManagerPlugin plugin;
        private DisplayManagerSettings editingClone;
        private string appearancePreset = SettingsAppearance.Midnight;
        private bool setupWizardCompleted;
        private int settingsSchemaVersion;
        private List<DisplayDeviceAlias> displayAliases = new List<DisplayDeviceAlias>();
        private List<DisplayInfo> availableDisplays = new List<DisplayInfo>();
        private GlobalHdrPolicy globalHdrPolicy = GlobalHdrPolicy.DoNotManage;
        private List<string> hdrMetadataMatchNames = HdrMetadataMatcher.DefaultMatchNames.ToList();
        private bool includeTagsInHdrMetadataMatch;
        private bool nativeHdrMigrationCompleted;
        private bool nativeHdrConflictNotified;
        private NightLightPolicy nightLightPolicy = NightLightPolicy.DoNotTouch;
        private RefreshRatePolicy globalRefreshRatePolicy = RefreshRatePolicy.Native;
        private double? preferredRefreshRateHz;
        private bool showDesktopTopPanel = true;
        private DesktopTopPanelDisplayMode desktopTopPanelDisplayMode = DesktopTopPanelDisplayMode.IconAndText;
        private bool enableAudioSwitcherHook = true;
        private bool showNotifications = true;
        private bool notifyNativeHdrConflict = true;

        public const int CurrentSettingsSchemaVersion = 2;

        public DisplayManagerSettings()
        {
        }

        public DisplayManagerSettings(PlayniteDisplayManagerPlugin plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<DisplayManagerSettings>();
            if (savedSettings != null)
            {
                AppearancePreset = savedSettings.AppearancePreset;
                SetupWizardCompleted = savedSettings.SetupWizardCompleted;
                SettingsSchemaVersion = savedSettings.SettingsSchemaVersion;
                DisplayAliases = savedSettings.DisplayAliases ?? new List<DisplayDeviceAlias>();
                GlobalHdrPolicy = savedSettings.GlobalHdrPolicy;
                HdrMetadataMatchNames = savedSettings.HdrMetadataMatchNames;
                IncludeTagsInHdrMetadataMatch = savedSettings.IncludeTagsInHdrMetadataMatch;
                NativeHdrMigrationCompleted = savedSettings.NativeHdrMigrationCompleted;
                NativeHdrConflictNotified = savedSettings.NativeHdrConflictNotified;
                NightLightPolicy = savedSettings.NightLightPolicy;
                GlobalRefreshRatePolicy = savedSettings.GlobalRefreshRatePolicy;
                PreferredRefreshRateHz = savedSettings.PreferredRefreshRateHz;
                ShowDesktopTopPanel = savedSettings.ShowDesktopTopPanel;
                DesktopTopPanelDisplayMode = savedSettings.DesktopTopPanelDisplayMode;
                EnableAudioSwitcherHook = savedSettings.EnableAudioSwitcherHook;
                ShowNotifications = savedSettings.ShowNotifications;
                NotifyNativeHdrConflict = savedSettings.NotifyNativeHdrConflict;
            }

            AppearancePreset = SettingsAppearance.Normalize(AppearancePreset);
            HdrMetadataMatchNames = HdrMetadataMatcher.NormalizeMatchNames(HdrMetadataMatchNames).ToList();
            MigrateRefreshRateLegacy();
            SettingsSchemaVersion = CurrentSettingsSchemaVersion;
            RefreshDisplays();
        }

        [DontSerialize]
        public PlayniteDisplayManagerPlugin Plugin => plugin;

        public string AppearancePreset
        {
            get => appearancePreset;
            set => SetValue(ref appearancePreset, value);
        }

        public bool SetupWizardCompleted
        {
            get => setupWizardCompleted;
            set => SetValue(ref setupWizardCompleted, value);
        }

        public int SettingsSchemaVersion
        {
            get => settingsSchemaVersion;
            set => SetValue(ref settingsSchemaVersion, value);
        }

        public GlobalHdrPolicy GlobalHdrPolicy
        {
            get => globalHdrPolicy;
            set => SetValue(ref globalHdrPolicy, value);
        }

        public List<string> HdrMetadataMatchNames
        {
            get => hdrMetadataMatchNames;
            set => SetValue(ref hdrMetadataMatchNames,
                HdrMetadataMatcher.NormalizeMatchNames(value).ToList());
        }

        public bool IncludeTagsInHdrMetadataMatch
        {
            get => includeTagsInHdrMetadataMatch;
            set => SetValue(ref includeTagsInHdrMetadataMatch, value);
        }

        public bool NativeHdrMigrationCompleted
        {
            get => nativeHdrMigrationCompleted;
            set => SetValue(ref nativeHdrMigrationCompleted, value);
        }

        public bool NativeHdrConflictNotified
        {
            get => nativeHdrConflictNotified;
            set => SetValue(ref nativeHdrConflictNotified, value);
        }

        public NightLightPolicy NightLightPolicy
        {
            get => nightLightPolicy;
            set => SetValue(ref nightLightPolicy, NightLightPolicy.DoNotTouch);
        }

        public RefreshRatePolicy GlobalRefreshRatePolicy
        {
            get => globalRefreshRatePolicy;
            set => SetValue(ref globalRefreshRatePolicy, value);
        }

        /// <summary>Target Hz when GlobalRefreshRatePolicy is ExactHz.</summary>
        public double? PreferredRefreshRateHz
        {
            get => preferredRefreshRateHz;
            set => SetValue(ref preferredRefreshRateHz, value);
        }

        public bool ShowDesktopTopPanel
        {
            get => showDesktopTopPanel;
            set => SetValue(ref showDesktopTopPanel, value);
        }

        public DesktopTopPanelDisplayMode DesktopTopPanelDisplayMode
        {
            get => desktopTopPanelDisplayMode;
            set => SetValue(ref desktopTopPanelDisplayMode, value);
        }

        public bool EnableAudioSwitcherHook
        {
            get => enableAudioSwitcherHook;
            set => SetValue(ref enableAudioSwitcherHook, value);
        }

        public bool ShowNotifications
        {
            get => showNotifications;
            set => SetValue(ref showNotifications, value);
        }

        public bool NotifyNativeHdrConflict
        {
            get => notifyNativeHdrConflict;
            set => SetValue(ref notifyNativeHdrConflict, value);
        }

        public List<DisplayDeviceAlias> DisplayAliases
        {
            get => displayAliases;
            set => SetValue(ref displayAliases, value ?? new List<DisplayDeviceAlias>());
        }

        [DontSerialize]
        public List<DisplayInfo> AvailableDisplays
        {
            get => availableDisplays;
            private set => SetValue(ref availableDisplays, value ?? new List<DisplayInfo>());
        }

        [DontSerialize]
        public string HdrMetadataMatchNamesText
        {
            get => string.Join(", ", HdrMetadataMatchNames ?? new List<string>());
            set
            {
                var parts = (value ?? string.Empty)
                    .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                HdrMetadataMatchNames = parts.ToList();
                OnPropertyChanged(nameof(HdrMetadataMatchNamesText));
            }
        }

        [DontSerialize]
        public List<AppearancePresetOption> AppearancePresetOptions => new List<AppearancePresetOption>
        {
            new AppearancePresetOption { Value = SettingsAppearance.Midnight, DisplayName = plugin?.Loc("LOCDisplayManager_PresetMidnight") ?? "Midnight" },
            new AppearancePresetOption { Value = SettingsAppearance.Paper, DisplayName = plugin?.Loc("LOCDisplayManager_PresetPaper") ?? "Paper" },
            new AppearancePresetOption { Value = SettingsAppearance.Oled, DisplayName = plugin?.Loc("LOCDisplayManager_PresetOled") ?? "OLED" },
            new AppearancePresetOption { Value = SettingsAppearance.Ocean, DisplayName = plugin?.Loc("LOCDisplayManager_PresetOcean") ?? "Ocean" },
            new AppearancePresetOption { Value = SettingsAppearance.Ember, DisplayName = plugin?.Loc("LOCDisplayManager_PresetEmber") ?? "Ember" }
        };

        [DontSerialize]
        public List<AppearancePresetOption> DesktopTopPanelDisplayModeOptions => new List<AppearancePresetOption>
        {
            new AppearancePresetOption
            {
                Value = nameof(DesktopTopPanelDisplayMode.Icon),
                DisplayName = plugin?.Loc("LOCDisplayManager_TopPanelModeIcon") ?? "Icon"
            },
            new AppearancePresetOption
            {
                Value = nameof(DesktopTopPanelDisplayMode.Text),
                DisplayName = plugin?.Loc("LOCDisplayManager_TopPanelModeText") ?? "Text"
            },
            new AppearancePresetOption
            {
                Value = nameof(DesktopTopPanelDisplayMode.IconAndText),
                DisplayName = plugin?.Loc("LOCDisplayManager_TopPanelModeIconAndText") ?? "Icon and text"
            }
        };

        [DontSerialize]
        public string DesktopTopPanelDisplayModeValue
        {
            get => DesktopTopPanelDisplayMode.ToString();
            set
            {
                if (Enum.TryParse(value, true, out DesktopTopPanelDisplayMode mode))
                {
                    DesktopTopPanelDisplayMode = mode;
                }
            }
        }

        public void RefreshDisplays()
        {
            if (plugin?.Displays == null)
            {
                AvailableDisplays = new List<DisplayInfo>();
                return;
            }

            AvailableDisplays = plugin.Displays.GetVisibleDisplays(DisplayAliases)
                .Select(d => d.Clone())
                .ToList();
            OnPropertyChanged(nameof(AvailableDisplays));
            OnPropertyChanged(nameof(ConnectedDisplayCount));
            OnPropertyChanged(nameof(PrimaryDisplayName));
        }

        [DontSerialize]
        public int ConnectedDisplayCount => AvailableDisplays?.Count(d => d.IsConnected) ?? 0;

        [DontSerialize]
        public string PrimaryDisplayName
        {
            get
            {
                var primary = AvailableDisplays?.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                    ?? AvailableDisplays?.FirstOrDefault(d => d.IsConnected);
                return primary?.EffectiveName;
            }
        }

        public void BeginEdit()
        {
            RefreshDisplays();
            editingClone = Serialization.GetClone(this);
        }

        public void CancelEdit()
        {
            if (editingClone == null)
            {
                return;
            }

            AppearancePreset = editingClone.AppearancePreset;
            SetupWizardCompleted = editingClone.SetupWizardCompleted;
            SettingsSchemaVersion = editingClone.SettingsSchemaVersion;
            DisplayAliases = editingClone.DisplayAliases ?? new List<DisplayDeviceAlias>();
            GlobalHdrPolicy = editingClone.GlobalHdrPolicy;
            HdrMetadataMatchNames = editingClone.HdrMetadataMatchNames;
            IncludeTagsInHdrMetadataMatch = editingClone.IncludeTagsInHdrMetadataMatch;
            NativeHdrMigrationCompleted = editingClone.NativeHdrMigrationCompleted;
            NativeHdrConflictNotified = editingClone.NativeHdrConflictNotified;
            NightLightPolicy = editingClone.NightLightPolicy;
            GlobalRefreshRatePolicy = editingClone.GlobalRefreshRatePolicy;
            PreferredRefreshRateHz = editingClone.PreferredRefreshRateHz;
            ShowDesktopTopPanel = editingClone.ShowDesktopTopPanel;
            DesktopTopPanelDisplayMode = editingClone.DesktopTopPanelDisplayMode;
            EnableAudioSwitcherHook = editingClone.EnableAudioSwitcherHook;
            ShowNotifications = editingClone.ShowNotifications;
            NotifyNativeHdrConflict = editingClone.NotifyNativeHdrConflict;
            editingClone = null;
            RefreshDisplays();
            OnPropertyChanged(nameof(HdrMetadataMatchNamesText));
            OnPropertyChanged(nameof(DesktopTopPanelDisplayModeValue));
        }

        public void EndEdit()
        {
            AppearancePreset = SettingsAppearance.Normalize(AppearancePreset);
            HdrMetadataMatchNames = HdrMetadataMatcher.NormalizeMatchNames(HdrMetadataMatchNames).ToList();
            MigrateRefreshRateLegacy();
            DisplayAliases = PersistAliases(AvailableDisplays, DisplayAliases);
            plugin.SavePluginSettings(this);
            plugin.ReloadSettings();
            editingClone = null;
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }

        private void MigrateRefreshRateLegacy()
        {
            var hz = PreferredRefreshRateHz;
            GlobalRefreshRatePolicy = RefreshRateService.NormalizeLegacyPolicy(GlobalRefreshRatePolicy, ref hz);
            PreferredRefreshRateHz = hz;
        }

        private List<DisplayDeviceAlias> PersistAliases(
            IEnumerable<DisplayInfo> currentDisplays,
            IEnumerable<DisplayDeviceAlias> existingAliases)
        {
            var devices = (currentDisplays ?? Enumerable.Empty<DisplayInfo>()).ToList();
            var currentIds = new HashSet<string>(devices.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);

            return devices
                .Where(HasMeaningfulCustomization)
                .Select(ToAlias)
                .Concat((existingAliases ?? Enumerable.Empty<DisplayDeviceAlias>())
                    .Where(alias => !string.IsNullOrWhiteSpace(alias.DisplayId) &&
                                    !currentIds.Contains(alias.DisplayId) &&
                                    DisplayEnumerator.HasMeaningfulAlias(alias))
                    .Select(SanitizeAlias))
                .ToList();
        }

        private static bool HasMeaningfulCustomization(DisplayInfo display)
        {
            return display != null &&
                   (!string.IsNullOrWhiteSpace(DisplayEnumerator.SanitizeCustomName(display.CustomName)) ||
                    !display.IsVisible);
        }

        private static DisplayDeviceAlias ToAlias(DisplayInfo display)
        {
            return new DisplayDeviceAlias
            {
                DisplayId = display.Id,
                CustomName = DisplayEnumerator.SanitizeCustomName(display.CustomName),
                LastKnownName = display.Name,
                IsVisible = display.IsVisible ? (bool?)null : false
            };
        }

        private static DisplayDeviceAlias SanitizeAlias(DisplayDeviceAlias alias)
        {
            return new DisplayDeviceAlias
            {
                DisplayId = alias.DisplayId,
                CustomName = DisplayEnumerator.SanitizeCustomName(alias.CustomName),
                LastKnownName = string.IsNullOrWhiteSpace(alias.LastKnownName) ? null : alias.LastKnownName.Trim(),
                Icon = string.IsNullOrWhiteSpace(alias.Icon) ? null : alias.Icon.Trim(),
                IsVisible = alias.IsVisible
            };
        }
    }
}
