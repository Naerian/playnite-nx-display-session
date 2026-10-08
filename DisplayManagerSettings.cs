using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Profiles;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Resolution;

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
        private GlobalHdrPolicy globalHdrPolicyFullscreen = GlobalHdrPolicy.DoNotManage;
        private List<string> hdrMetadataMatchNames = HdrMetadataMatcher.DefaultMatchNames.ToList();
        private bool includeTagsInHdrMetadataMatch;
        private bool nativeHdrMigrationCompleted;
        private bool nativeHdrConflictNotified;
        private RefreshRatePolicy globalRefreshRatePolicy = RefreshRatePolicy.Native;
        private RefreshRatePolicy globalRefreshRatePolicyFullscreen = RefreshRatePolicy.Native;
        private double? preferredRefreshRateHz;
        private double? preferredRefreshRateHzFullscreen;
        private ResolutionPolicy globalResolutionPolicy = ResolutionPolicy.Native;
        private ResolutionPolicy globalResolutionPolicyFullscreen = ResolutionPolicy.Native;
        private int? preferredResolutionWidth;
        private int? preferredResolutionHeight;
        private int? preferredResolutionWidthFullscreen;
        private int? preferredResolutionHeightFullscreen;
        private int postChangeSettleDelayMs = 1000;
        private int preDisplayChangeDelayMs;
        private int preferredDisplayWaitMs;
        private bool showDesktopTopPanel = true;
        private DesktopTopPanelDisplayMode desktopTopPanelDisplayMode = DesktopTopPanelDisplayMode.Icon;
        private bool showNotifications = true;
        private bool notifyNativeHdrConflict = true;
        private string preferredPlayDisplayId;
        private bool turnOffOtherDisplaysOnLaunch;
        private List<DisplayProfile> displayProfiles = new List<DisplayProfile>();
        private Guid? defaultDisplayProfileId;
        private Guid? defaultDisplayProfileIdFullscreen;
        private bool relocatePlayniteFullscreenAfterRestore;
        private bool applyTopologyOnFullscreenMode;
        private bool enableVerboseLogging;
        private List<GameDisplayProfileEntry> availableGameProfiles = new List<GameDisplayProfileEntry>();
        private List<PlatformProfileEntry> availablePlatformProfiles = new List<PlatformProfileEntry>();

        public const int CurrentSettingsSchemaVersion = 7;

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
                GlobalHdrPolicyFullscreen = savedSettings.SettingsSchemaVersion >= 7
                    ? savedSettings.GlobalHdrPolicyFullscreen
                    : savedSettings.GlobalHdrPolicy;
                HdrMetadataMatchNames = savedSettings.HdrMetadataMatchNames;
                IncludeTagsInHdrMetadataMatch = savedSettings.IncludeTagsInHdrMetadataMatch;
                NativeHdrMigrationCompleted = savedSettings.NativeHdrMigrationCompleted;
                NativeHdrConflictNotified = savedSettings.NativeHdrConflictNotified;
                GlobalRefreshRatePolicy = savedSettings.GlobalRefreshRatePolicy;
                GlobalRefreshRatePolicyFullscreen = savedSettings.SettingsSchemaVersion >= 7
                    ? savedSettings.GlobalRefreshRatePolicyFullscreen
                    : savedSettings.GlobalRefreshRatePolicy;
                PreferredRefreshRateHz = savedSettings.PreferredRefreshRateHz;
                PreferredRefreshRateHzFullscreen = savedSettings.SettingsSchemaVersion >= 7
                    ? savedSettings.PreferredRefreshRateHzFullscreen
                    : savedSettings.PreferredRefreshRateHz;
                GlobalResolutionPolicy = savedSettings.GlobalResolutionPolicy;
                GlobalResolutionPolicyFullscreen = savedSettings.SettingsSchemaVersion >= 7
                    ? savedSettings.GlobalResolutionPolicyFullscreen
                    : savedSettings.GlobalResolutionPolicy;
                PreferredResolutionWidth = savedSettings.PreferredResolutionWidth;
                PreferredResolutionHeight = savedSettings.PreferredResolutionHeight;
                PreferredResolutionWidthFullscreen = savedSettings.SettingsSchemaVersion >= 7
                    ? savedSettings.PreferredResolutionWidthFullscreen
                    : savedSettings.PreferredResolutionWidth;
                PreferredResolutionHeightFullscreen = savedSettings.SettingsSchemaVersion >= 7
                    ? savedSettings.PreferredResolutionHeightFullscreen
                    : savedSettings.PreferredResolutionHeight;
                PostChangeSettleDelayMs = savedSettings.PostChangeSettleDelayMs;
                PreDisplayChangeDelayMs = savedSettings.PreDisplayChangeDelayMs;
                PreferredDisplayWaitMs = savedSettings.PreferredDisplayWaitMs;
                ShowDesktopTopPanel = savedSettings.ShowDesktopTopPanel;
                DesktopTopPanelDisplayMode = savedSettings.DesktopTopPanelDisplayMode;
                ShowNotifications = savedSettings.ShowNotifications;
                NotifyNativeHdrConflict = savedSettings.NotifyNativeHdrConflict;
                PreferredPlayDisplayId = savedSettings.PreferredPlayDisplayId;
                TurnOffOtherDisplaysOnLaunch = savedSettings.TurnOffOtherDisplaysOnLaunch;
                DisplayProfiles = savedSettings.DisplayProfiles ?? new List<DisplayProfile>();
                DefaultDisplayProfileId = savedSettings.DefaultDisplayProfileId;
                DefaultDisplayProfileIdFullscreen = savedSettings.DefaultDisplayProfileIdFullscreen;
                RelocatePlayniteFullscreenAfterRestore = savedSettings.RelocatePlayniteFullscreenAfterRestore;
                ApplyTopologyOnFullscreenMode = savedSettings.ApplyTopologyOnFullscreenMode;
                EnableVerboseLogging = savedSettings.EnableVerboseLogging;
            }

            AppearancePreset = SettingsAppearance.Normalize(AppearancePreset);
            HdrMetadataMatchNames = HdrMetadataMatcher.NormalizeMatchNames(HdrMetadataMatchNames).ToList();
            MigrateRefreshRateLegacy();
            MigrateDisplayProfiles();
            SyncLegacyFieldsFromDefaultDisplayProfile();
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

        /// <summary>HDR policy when launching from Playnite Desktop.</summary>
        public GlobalHdrPolicy GlobalHdrPolicy
        {
            get => globalHdrPolicy;
            set => SetValue(ref globalHdrPolicy, value);
        }

        /// <summary>HDR policy when launching from Playnite Fullscreen.</summary>
        public GlobalHdrPolicy GlobalHdrPolicyFullscreen
        {
            get => globalHdrPolicyFullscreen;
            set => SetValue(ref globalHdrPolicyFullscreen, value);
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

        /// <summary>Refresh policy when launching from Playnite Desktop.</summary>
        public RefreshRatePolicy GlobalRefreshRatePolicy
        {
            get => globalRefreshRatePolicy;
            set => SetValue(ref globalRefreshRatePolicy, value);
        }

        /// <summary>Refresh policy when launching from Playnite Fullscreen.</summary>
        public RefreshRatePolicy GlobalRefreshRatePolicyFullscreen
        {
            get => globalRefreshRatePolicyFullscreen;
            set => SetValue(ref globalRefreshRatePolicyFullscreen, value);
        }

        /// <summary>Target Hz when Desktop GlobalRefreshRatePolicy is ExactHz.</summary>
        public double? PreferredRefreshRateHz
        {
            get => preferredRefreshRateHz;
            set => SetValue(ref preferredRefreshRateHz, value);
        }

        /// <summary>Target Hz when Fullscreen GlobalRefreshRatePolicyFullscreen is ExactHz.</summary>
        public double? PreferredRefreshRateHzFullscreen
        {
            get => preferredRefreshRateHzFullscreen;
            set => SetValue(ref preferredRefreshRateHzFullscreen, value);
        }

        /// <summary>Resolution policy when launching from Playnite Desktop.</summary>
        public ResolutionPolicy GlobalResolutionPolicy
        {
            get => globalResolutionPolicy;
            set => SetValue(ref globalResolutionPolicy, value);
        }

        /// <summary>Resolution policy when launching from Playnite Fullscreen.</summary>
        public ResolutionPolicy GlobalResolutionPolicyFullscreen
        {
            get => globalResolutionPolicyFullscreen;
            set => SetValue(ref globalResolutionPolicyFullscreen, value);
        }

        public int? PreferredResolutionWidth
        {
            get => preferredResolutionWidth;
            set => SetValue(ref preferredResolutionWidth, value);
        }

        public int? PreferredResolutionHeight
        {
            get => preferredResolutionHeight;
            set => SetValue(ref preferredResolutionHeight, value);
        }

        public int? PreferredResolutionWidthFullscreen
        {
            get => preferredResolutionWidthFullscreen;
            set => SetValue(ref preferredResolutionWidthFullscreen, value);
        }

        public int? PreferredResolutionHeightFullscreen
        {
            get => preferredResolutionHeightFullscreen;
            set => SetValue(ref preferredResolutionHeightFullscreen, value);
        }

        public GlobalHdrPolicy GetHdrPolicyForMode(ApplicationMode mode)
        {
            return mode == ApplicationMode.Fullscreen ? GlobalHdrPolicyFullscreen : GlobalHdrPolicy;
        }

        public void SetHdrPolicyForMode(ApplicationMode mode, GlobalHdrPolicy policy)
        {
            if (mode == ApplicationMode.Fullscreen)
            {
                GlobalHdrPolicyFullscreen = policy;
            }
            else
            {
                GlobalHdrPolicy = policy;
            }
        }

        public RefreshRatePolicy GetRefreshRatePolicyForMode(ApplicationMode mode)
        {
            return mode == ApplicationMode.Fullscreen
                ? GlobalRefreshRatePolicyFullscreen
                : GlobalRefreshRatePolicy;
        }

        public void SetRefreshRatePolicyForMode(ApplicationMode mode, RefreshRatePolicy policy)
        {
            if (mode == ApplicationMode.Fullscreen)
            {
                GlobalRefreshRatePolicyFullscreen = policy;
            }
            else
            {
                GlobalRefreshRatePolicy = policy;
            }
        }

        public double? GetPreferredRefreshRateHzForMode(ApplicationMode mode)
        {
            return mode == ApplicationMode.Fullscreen
                ? PreferredRefreshRateHzFullscreen
                : PreferredRefreshRateHz;
        }

        public void SetPreferredRefreshRateHzForMode(ApplicationMode mode, double? hz)
        {
            if (mode == ApplicationMode.Fullscreen)
            {
                PreferredRefreshRateHzFullscreen = hz;
            }
            else
            {
                PreferredRefreshRateHz = hz;
            }
        }

        public ResolutionPolicy GetResolutionPolicyForMode(ApplicationMode mode)
        {
            return mode == ApplicationMode.Fullscreen
                ? GlobalResolutionPolicyFullscreen
                : GlobalResolutionPolicy;
        }

        public void SetResolutionPolicyForMode(ApplicationMode mode, ResolutionPolicy policy)
        {
            if (mode == ApplicationMode.Fullscreen)
            {
                GlobalResolutionPolicyFullscreen = policy;
            }
            else
            {
                GlobalResolutionPolicy = policy;
            }
        }

        public void GetPreferredResolutionForMode(ApplicationMode mode, out int? width, out int? height)
        {
            if (mode == ApplicationMode.Fullscreen)
            {
                width = PreferredResolutionWidthFullscreen;
                height = PreferredResolutionHeightFullscreen;
            }
            else
            {
                width = PreferredResolutionWidth;
                height = PreferredResolutionHeight;
            }
        }

        public void SetPreferredResolutionForMode(ApplicationMode mode, int? width, int? height)
        {
            if (mode == ApplicationMode.Fullscreen)
            {
                PreferredResolutionWidthFullscreen = width;
                PreferredResolutionHeightFullscreen = height;
            }
            else
            {
                PreferredResolutionWidth = width;
                PreferredResolutionHeight = height;
            }
        }

        public int PostChangeSettleDelayMs
        {
            get => postChangeSettleDelayMs;
            set => SetValue(ref postChangeSettleDelayMs, Math.Max(0, value));
        }

        /// <summary>
        /// Wait before applying topology/resolution/HDR so Playnite can finish minimizing first.
        /// </summary>
        public int PreDisplayChangeDelayMs
        {
            get => preDisplayChangeDelayMs;
            set => SetValue(ref preDisplayChangeDelayMs, Math.Max(0, value));
        }

        /// <summary>
        /// How long to wait/retry for the preferred play display before applying missing-display policy.
        /// </summary>
        public int PreferredDisplayWaitMs
        {
            get => preferredDisplayWaitMs;
            set => SetValue(ref preferredDisplayWaitMs, Math.Max(0, value));
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

        /// <summary>Stable display id to make primary when a game launches.</summary>
        public string PreferredPlayDisplayId
        {
            get => preferredPlayDisplayId;
            set => SetValue(ref preferredPlayDisplayId, value);
        }

        public bool TurnOffOtherDisplaysOnLaunch
        {
            get => turnOffOtherDisplaysOnLaunch;
            set => SetValue(ref turnOffOtherDisplaysOnLaunch, value);
        }

        [DataMember(Name = "topologyProfiles")]
        public List<DisplayProfile> DisplayProfiles
        {
            get => displayProfiles;
            set => SetValue(ref displayProfiles, value ?? new List<DisplayProfile>());
        }

        /// <summary>
        /// Launch default display profile for Playnite Desktop mode (and legacy single-default JSON).
        /// </summary>
        [DataMember(Name = "defaultTopologyProfileId")]
        public Guid? DefaultDisplayProfileId
        {
            get => defaultDisplayProfileId;
            set => SetValue(ref defaultDisplayProfileId, value);
        }

        /// <summary>
        /// Launch default display profile for Playnite Fullscreen mode.
        /// Migrates to <see cref="DefaultDisplayProfileId"/> when missing or invalid.
        /// </summary>
        [DataMember(Name = "defaultTopologyProfileIdFullscreen")]
        public Guid? DefaultDisplayProfileIdFullscreen
        {
            get => defaultDisplayProfileIdFullscreen;
            set => SetValue(ref defaultDisplayProfileIdFullscreen, value);
        }

        public bool RelocatePlayniteFullscreenAfterRestore
        {
            get => relocatePlayniteFullscreenAfterRestore;
            set => SetValue(ref relocatePlayniteFullscreenAfterRestore, value);
        }

        /// <summary>
        /// Legacy setting (ignored). Fullscreen mode always applies the Fullscreen primary
        /// on enter and keeps that layout for the mode session.
        /// </summary>
        public bool ApplyTopologyOnFullscreenMode
        {
            get => applyTopologyOnFullscreenMode;
            set => SetValue(ref applyTopologyOnFullscreenMode, value);
        }

        /// <summary>When true, Debug/Trace lines are also written to the support log file.</summary>
        public bool EnableVerboseLogging
        {
            get => enableVerboseLogging;
            set => SetValue(ref enableVerboseLogging, value);
        }

        [DontSerialize]
        public List<GameDisplayProfileEntry> AvailableGameProfiles
        {
            get => availableGameProfiles;
            set => SetValue(ref availableGameProfiles, value ?? new List<GameDisplayProfileEntry>());
        }

        [DontSerialize]
        public List<PlatformProfileEntry> AvailablePlatformProfiles
        {
            get => availablePlatformProfiles;
            set => SetValue(ref availablePlatformProfiles, value ?? new List<PlatformProfileEntry>());
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

        [DontSerialize]
        public List<AppearancePresetOption> PostChangeSettleDelayOptions => new List<AppearancePresetOption>
        {
            new AppearancePresetOption { Value = "0", DisplayName = "0 ms" },
            new AppearancePresetOption { Value = "500", DisplayName = "500 ms" },
            new AppearancePresetOption { Value = "1000", DisplayName = "1000 ms" },
            new AppearancePresetOption { Value = "2000", DisplayName = "2000 ms" },
            new AppearancePresetOption { Value = "3000", DisplayName = "3000 ms" },
            new AppearancePresetOption { Value = "5000", DisplayName = "5000 ms" }
        };

        [DontSerialize]
        public string PostChangeSettleDelayMsValue
        {
            get => PostChangeSettleDelayMs.ToString();
            set
            {
                if (int.TryParse(value, out var ms))
                {
                    PostChangeSettleDelayMs = ms;
                }
            }
        }

        [DontSerialize]
        public List<AppearancePresetOption> PreDisplayChangeDelayOptions => new List<AppearancePresetOption>
        {
            new AppearancePresetOption { Value = "0", DisplayName = "0 ms" },
            new AppearancePresetOption { Value = "500", DisplayName = "500 ms" },
            new AppearancePresetOption { Value = "1000", DisplayName = "1000 ms" },
            new AppearancePresetOption { Value = "2000", DisplayName = "2000 ms" },
            new AppearancePresetOption { Value = "3000", DisplayName = "3000 ms" },
            new AppearancePresetOption { Value = "5000", DisplayName = "5000 ms" }
        };

        [DontSerialize]
        public string PreDisplayChangeDelayMsValue
        {
            get => PreDisplayChangeDelayMs.ToString();
            set
            {
                if (int.TryParse(value, out var ms))
                {
                    PreDisplayChangeDelayMs = ms;
                }
            }
        }

        [DontSerialize]
        public List<AppearancePresetOption> PreferredDisplayWaitOptions => new List<AppearancePresetOption>
        {
            new AppearancePresetOption { Value = "0", DisplayName = "0 s" },
            new AppearancePresetOption { Value = "3000", DisplayName = "3 s" },
            new AppearancePresetOption { Value = "5000", DisplayName = "5 s" },
            new AppearancePresetOption { Value = "8000", DisplayName = "8 s" },
            new AppearancePresetOption { Value = "10000", DisplayName = "10 s" },
            new AppearancePresetOption { Value = "15000", DisplayName = "15 s" }
        };

        [DontSerialize]
        public string PreferredDisplayWaitMsValue
        {
            get => PreferredDisplayWaitMs.ToString();
            set
            {
                if (int.TryParse(value, out var ms))
                {
                    PreferredDisplayWaitMs = ms;
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
            AvailableGameProfiles = plugin?.GetGameProfileEntries() ?? new List<GameDisplayProfileEntry>();
            AvailablePlatformProfiles = plugin?.GetPlatformProfileEntries() ?? new List<PlatformProfileEntry>();
            editingClone = Serialization.GetClone(this);
            DisplayProfiles = (DisplayProfiles ?? new List<DisplayProfile>())
                .Select(p => p?.Clone())
                .Where(p => p != null)
                .ToList();
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
            GlobalHdrPolicyFullscreen = editingClone.GlobalHdrPolicyFullscreen;
            HdrMetadataMatchNames = editingClone.HdrMetadataMatchNames;
            IncludeTagsInHdrMetadataMatch = editingClone.IncludeTagsInHdrMetadataMatch;
            NativeHdrMigrationCompleted = editingClone.NativeHdrMigrationCompleted;
            NativeHdrConflictNotified = editingClone.NativeHdrConflictNotified;
            GlobalRefreshRatePolicy = editingClone.GlobalRefreshRatePolicy;
            GlobalRefreshRatePolicyFullscreen = editingClone.GlobalRefreshRatePolicyFullscreen;
            PreferredRefreshRateHz = editingClone.PreferredRefreshRateHz;
            PreferredRefreshRateHzFullscreen = editingClone.PreferredRefreshRateHzFullscreen;
            GlobalResolutionPolicy = editingClone.GlobalResolutionPolicy;
            GlobalResolutionPolicyFullscreen = editingClone.GlobalResolutionPolicyFullscreen;
            PreferredResolutionWidth = editingClone.PreferredResolutionWidth;
            PreferredResolutionHeight = editingClone.PreferredResolutionHeight;
            PreferredResolutionWidthFullscreen = editingClone.PreferredResolutionWidthFullscreen;
            PreferredResolutionHeightFullscreen = editingClone.PreferredResolutionHeightFullscreen;
            PostChangeSettleDelayMs = editingClone.PostChangeSettleDelayMs;
            PreDisplayChangeDelayMs = editingClone.PreDisplayChangeDelayMs;
            PreferredDisplayWaitMs = editingClone.PreferredDisplayWaitMs;
            ShowDesktopTopPanel = editingClone.ShowDesktopTopPanel;
            DesktopTopPanelDisplayMode = editingClone.DesktopTopPanelDisplayMode;
            ShowNotifications = editingClone.ShowNotifications;
            NotifyNativeHdrConflict = editingClone.NotifyNativeHdrConflict;
            PreferredPlayDisplayId = editingClone.PreferredPlayDisplayId;
            TurnOffOtherDisplaysOnLaunch = editingClone.TurnOffOtherDisplaysOnLaunch;
            DisplayProfiles = editingClone.DisplayProfiles ?? new List<DisplayProfile>();
            DefaultDisplayProfileId = editingClone.DefaultDisplayProfileId;
            DefaultDisplayProfileIdFullscreen = editingClone.DefaultDisplayProfileIdFullscreen;
            RelocatePlayniteFullscreenAfterRestore = editingClone.RelocatePlayniteFullscreenAfterRestore;
            ApplyTopologyOnFullscreenMode = editingClone.ApplyTopologyOnFullscreenMode;
            EnableVerboseLogging = editingClone.EnableVerboseLogging;
            editingClone = null;
            AvailableGameProfiles = plugin?.GetGameProfileEntries() ?? new List<GameDisplayProfileEntry>();
            AvailablePlatformProfiles = plugin?.GetPlatformProfileEntries() ?? new List<PlatformProfileEntry>();
            RefreshDisplays();
            OnPropertyChanged(nameof(HdrMetadataMatchNamesText));
            OnPropertyChanged(nameof(DesktopTopPanelDisplayModeValue));
            OnPropertyChanged(nameof(PostChangeSettleDelayMsValue));
            OnPropertyChanged(nameof(PreDisplayChangeDelayMsValue));
            OnPropertyChanged(nameof(PreferredDisplayWaitMsValue));
        }

        public void EndEdit()
        {
            AppearancePreset = SettingsAppearance.Normalize(AppearancePreset);
            HdrMetadataMatchNames = HdrMetadataMatcher.NormalizeMatchNames(HdrMetadataMatchNames).ToList();
            MigrateRefreshRateLegacy();
            MigrateDisplayProfiles();
            SyncLegacyFieldsFromDefaultDisplayProfile();
            DisplayAliases = PersistAliases(AvailableDisplays, DisplayAliases);
            plugin.ReplaceGameProfiles(AvailableGameProfiles);
            plugin.ReplacePlatformProfiles(AvailablePlatformProfiles);
            plugin.SavePluginSettings(this);
            plugin.ReloadSettings();
            plugin.LogInfo("Settings saved.");
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

            var hzFs = PreferredRefreshRateHzFullscreen;
            GlobalRefreshRatePolicyFullscreen =
                RefreshRateService.NormalizeLegacyPolicy(GlobalRefreshRatePolicyFullscreen, ref hzFs);
            PreferredRefreshRateHzFullscreen = hzFs;
        }

        public void MigrateDisplayProfilesPublic()
        {
            MigrateDisplayProfiles();
        }

        private void MigrateDisplayProfiles()
        {
            if (DisplayProfiles == null)
            {
                DisplayProfiles = new List<DisplayProfile>();
            }

            var needsRewrite = DisplayProfiles.Any(p =>
                p == null || p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name));
            if (needsRewrite)
            {
                DisplayProfiles = DisplayProfiles
                    .Where(p => p != null)
                    .Select(p =>
                    {
                        if (p.Id == Guid.Empty)
                        {
                            p.Id = Guid.NewGuid();
                        }

                        if (string.IsNullOrWhiteSpace(p.Name))
                        {
                            p.Name = "Default";
                        }

                        return p;
                    })
                    .ToList();
            }

            if (DisplayProfiles.Count == 0)
            {
                var defaults = new DisplayProfile
                {
                    Id = Guid.NewGuid(),
                    Name = plugin?.Loc("LOCDisplayManager_DisplayProfileDefaultName") ?? "Default",
                    PreferredPlayDisplayId = PreferredPlayDisplayId,
                    TurnOffOtherDisplays = false,
                    MissingDisplayPolicy = MissingDisplayPolicy.UseWindowsPrimary
                };
                DisplayProfiles.Add(defaults);
                DisplayProfiles.Add(CreateNamedDisplayProfile("LOCDisplayManager_DisplayProfileSoloTvName", "Solo TV", true));
                DisplayProfiles.Add(CreateNamedDisplayProfile("LOCDisplayManager_DisplayProfilePcDesktopName", "PC / Desktop", false));
                DefaultDisplayProfileId = defaults.Id;
            }
            else
            {
                EnsureNamedDisplayProfile("LOCDisplayManager_DisplayProfileSoloTvName", "Solo TV", true);
                EnsureNamedDisplayProfile("LOCDisplayManager_DisplayProfilePcDesktopName", "PC / Desktop", false);
            }

            if (!DefaultDisplayProfileId.HasValue
                || DisplayProfiles.All(p => p.Id != DefaultDisplayProfileId.Value))
            {
                DefaultDisplayProfileId = DisplayProfiles[0].Id;
            }

            if (!DefaultDisplayProfileIdFullscreen.HasValue
                || DisplayProfiles.All(p => p.Id != DefaultDisplayProfileIdFullscreen.Value))
            {
                DefaultDisplayProfileIdFullscreen = DefaultDisplayProfileId;
            }

            EnsureDistinctFullscreenLaunchProfile();
        }

        /// <summary>
        /// Desktop and Fullscreen launch defaults must be separate profiles so preferred
        /// display and topology can diverge without sharing the same object.
        /// </summary>
        private void EnsureDistinctFullscreenLaunchProfile()
        {
            if (!DefaultDisplayProfileId.HasValue
                || !DefaultDisplayProfileIdFullscreen.HasValue
                || DefaultDisplayProfileId.Value != DefaultDisplayProfileIdFullscreen.Value)
            {
                return;
            }

            var source = DisplayProfiles.FirstOrDefault(p => p.Id == DefaultDisplayProfileId.Value);
            if (source == null)
            {
                return;
            }

            var clone = source.Clone();
            clone.Id = Guid.NewGuid();
            clone.Name = plugin?.Loc("LOCDisplayManager_DisplayProfileFullscreenLaunchName")
                ?? "Fullscreen launch";
            DisplayProfiles.Add(clone);
            DefaultDisplayProfileIdFullscreen = clone.Id;
        }

        /// <summary>Desktop launch default (legacy name).</summary>
        public DisplayProfile GetDefaultDisplayProfile()
        {
            return GetDefaultDisplayProfileForMode(ApplicationMode.Desktop);
        }

        public DisplayProfile GetDefaultDisplayProfileForMode(ApplicationMode mode)
        {
            MigrateDisplayProfiles();
            var id = mode == ApplicationMode.Fullscreen
                ? DefaultDisplayProfileIdFullscreen
                : DefaultDisplayProfileId;
            return DisplayProfiles.FirstOrDefault(p => id.HasValue && p.Id == id.Value)
                ?? DisplayProfiles.FirstOrDefault(p => p.Id == DefaultDisplayProfileId)
                ?? DisplayProfiles.FirstOrDefault();
        }

        public DisplayProfile GetDisplayProfile(Guid? id)
        {
            if (!id.HasValue || id.Value == Guid.Empty)
            {
                return null;
            }

            return DisplayProfiles?.FirstOrDefault(p => p.Id == id.Value);
        }

        public void SyncLegacyFieldsFromDefaultDisplayProfile()
        {
            // Do not call GetDefaultDisplayProfile() here — it re-enters MigrateDisplayProfiles.
            var defaults = DisplayProfiles?.FirstOrDefault(p =>
                               DefaultDisplayProfileId.HasValue && p.Id == DefaultDisplayProfileId.Value)
                           ?? DisplayProfiles?.FirstOrDefault();
            if (defaults == null)
            {
                return;
            }

            PreferredPlayDisplayId = defaults.PreferredPlayDisplayId;
            TurnOffOtherDisplaysOnLaunch = defaults.TurnOffOtherDisplays;
        }

        public void SyncLegacyFieldsFromDefaultTopology()
        {
            SyncLegacyFieldsFromDefaultDisplayProfile();
        }

        private DisplayProfile CreateNamedDisplayProfile(string locKey, string fallbackName, bool turnOffOthers)
        {
            return new DisplayProfile
            {
                Id = Guid.NewGuid(),
                Name = plugin?.Loc(locKey) ?? fallbackName,
                TurnOffOtherDisplays = turnOffOthers,
                MissingDisplayPolicy = MissingDisplayPolicy.UseWindowsPrimary
            };
        }

        private void EnsureNamedDisplayProfile(string locKey, string fallbackName, bool turnOffOthers)
        {
            var name = plugin?.Loc(locKey) ?? fallbackName;
            if (DisplayProfiles.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(p.Name, fallbackName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            DisplayProfiles.Add(CreateNamedDisplayProfile(locKey, fallbackName, turnOffOthers));
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
