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
        private bool showDesktopTopPanel = true;

        public const int CurrentSettingsSchemaVersion = 1;

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
                ShowDesktopTopPanel = savedSettings.ShowDesktopTopPanel;
            }

            AppearancePreset = SettingsAppearance.Normalize(AppearancePreset);
            HdrMetadataMatchNames = HdrMetadataMatcher.NormalizeMatchNames(HdrMetadataMatchNames).ToList();
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

        /// <summary>Feature/Tag names that indicate HDR (case-insensitive). Defaults: HDR, HDR10, …</summary>
        public List<string> HdrMetadataMatchNames
        {
            get => hdrMetadataMatchNames;
            set => SetValue(ref hdrMetadataMatchNames,
                HdrMetadataMatcher.NormalizeMatchNames(value).ToList());
        }

        /// <summary>When true, Tags are scanned in addition to Features for policy 3.</summary>
        public bool IncludeTagsInHdrMetadataMatch
        {
            get => includeTagsInHdrMetadataMatch;
            set => SetValue(ref includeTagsInHdrMetadataMatch, value);
        }

        /// <summary>True after the user finished (or skipped) the setup wizard migration step at least once.</summary>
        public bool NativeHdrMigrationCompleted
        {
            get => nativeHdrMigrationCompleted;
            set => SetValue(ref nativeHdrMigrationCompleted, value);
        }

        /// <summary>One-shot toast when NX clears a conflicting EnableSystemHdr at launch.</summary>
        public bool NativeHdrConflictNotified
        {
            get => nativeHdrConflictNotified;
            set => SetValue(ref nativeHdrConflictNotified, value);
        }

        /// <summary>v1 only supports DoNotTouch — see NightLightStatus.</summary>
        public NightLightPolicy NightLightPolicy
        {
            get => nightLightPolicy;
            set => SetValue(ref nightLightPolicy, NightLightPolicy.DoNotTouch);
        }

        /// <summary>Optional refresh-rate preference for game sessions (primary, same resolution).</summary>
        public RefreshRatePolicy GlobalRefreshRatePolicy
        {
            get => globalRefreshRatePolicy;
            set => SetValue(ref globalRefreshRatePolicy, value);
        }

        /// <summary>Desktop top-panel button (opens settings).</summary>
        public bool ShowDesktopTopPanel
        {
            get => showDesktopTopPanel;
            set => SetValue(ref showDesktopTopPanel, value);
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
            ShowDesktopTopPanel = editingClone.ShowDesktopTopPanel;
            editingClone = null;
            RefreshDisplays();
            OnPropertyChanged(nameof(HdrMetadataMatchNamesText));
        }

        public void EndEdit()
        {
            AppearancePreset = SettingsAppearance.Normalize(AppearancePreset);
            HdrMetadataMatchNames = HdrMetadataMatcher.NormalizeMatchNames(HdrMetadataMatchNames).ToList();
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
