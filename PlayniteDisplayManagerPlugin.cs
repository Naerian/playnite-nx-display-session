using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Logging;
using PlayniteDisplayManager.Profiles;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Restore;
using PlayniteDisplayManager.Resolution;
using PlayniteDisplayManager.Theme;

namespace PlayniteDisplayManager
{
    public sealed class PlayniteDisplayManagerPlugin : GenericPlugin
    {
        private readonly ILogger logger;
        private readonly PluginFileLogger fileLogger;
        private readonly HdrService hdr = new HdrService();
        private readonly RefreshRateService refreshRates = new RefreshRateService();
        private readonly ResolutionService resolutions = new ResolutionService();
        private DisplayManagerSettings settings;
        private GameDisplayProfileStore gameProfiles;
        private PlatformProfileStore platformProfiles;
        private NativeHdrFlagMigration nativeHdrMigration;
        private ResourceDictionary englishFallbackResources;
        private DisplayRestoreClient restoreClient;
        private DispatcherTimer restoreHeartbeatTimer;
        private DisplaySnapshot gameSessionSnapshot;
        private DisplaySnapshot modeSessionSnapshot;
        private Guid? activeGameId;
        private string activeGameName;
        private TopPanelItem desktopTopPanelItem;
        private bool openingStandaloneSettings;

        public override Guid Id { get; } = Guid.Parse("9c2e4a71-b8d3-4f6a-a1c5-0e7d92f3b846");

        public DisplayEnumerator Displays { get; }

        public DisplayTopologyService Topology { get; }

        public HdrService Hdr => hdr;

        public RefreshRateService RefreshRates => refreshRates;

        public ResolutionService Resolutions => resolutions;

        public GameDisplayProfileStore GameProfiles => gameProfiles;

        public PlatformProfileStore PlatformProfiles => platformProfiles;

        public DisplayManagerThemeApi Theme { get; }

        public bool IsSessionActive => activeGameId.HasValue;

        public bool IsModeSessionActive => modeSessionSnapshot != null;

        public string ActiveGameName => activeGameName;

        public PlayniteDisplayManagerPlugin(IPlayniteAPI playniteApi) : base(playniteApi)
        {
            var playniteLogger = LogManager.GetLogger();
            fileLogger = new PluginFileLogger(
                GetPluginUserDataPath(),
                playniteLogger,
                () => settings?.EnableVerboseLogging == true);
            logger = fileLogger;
            Displays = new DisplayEnumerator();
            Topology = new DisplayTopologyService();
            gameProfiles = new GameDisplayProfileStore(GetPluginUserDataPath());
            platformProfiles = new PlatformProfileStore(GetPluginUserDataPath());
            nativeHdrMigration = new NativeHdrFlagMigration(PlayniteApi, logger, GetPluginUserDataPath());
            Theme = new DisplayManagerThemeApi(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                SourceName = "DisplayManager",
                ElementList = new List<string>
                {
                    "DisplayList",
                    "HdrStatus",
                    "ActiveProfile",
                    "SessionStatus",
                    "DisplaysSummary",
                    "OpenSettingsButton"
                }
            });

            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = "DisplayManager",
                SettingsRoot = nameof(Theme)
            });

            EnsureEnglishFallbackResources();
            ReloadSettings();
            Theme.Refresh();
            logger.Info("Display Manager loaded. Support log: " + fileLogger.LogFilePath);
        }

        public string SupportLogFilePath => fileLogger?.LogFilePath;

        public string SupportLogDirectory => fileLogger?.LogDirectory;

        public void LogInfo(string message)
        {
            logger?.Info(message);
        }

        public bool TryOpenSupportLogFolder(out string error)
        {
            error = null;
            try
            {
                var dir = SupportLogDirectory;
                if (string.IsNullOrWhiteSpace(dir))
                {
                    error = "Log directory is not available.";
                    return false;
                }

                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Warn(ex, "Failed to open support log folder.");
                return false;
            }
        }

        public bool TryOpenSupportLogFile(out string error)
        {
            error = null;
            try
            {
                var path = SupportLogFilePath;
                if (string.IsNullOrWhiteSpace(path) || fileLogger == null)
                {
                    error = "Log file path is not available.";
                    return false;
                }

                fileLogger.EnsureFileExists();
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Warn(ex, "Failed to open support log file.");
                return false;
            }
        }

        public bool TryClearSupportLog(out string error)
        {
            error = null;
            try
            {
                if (fileLogger == null)
                {
                    error = "Log file path is not available.";
                    return false;
                }

                fileLogger.Clear();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Warn(ex, "Failed to clear support log.");
                return false;
            }
        }

        public bool TryCopySupportLogPath(out string error)
        {
            error = null;
            try
            {
                var path = SupportLogFilePath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Log file path is not available.";
                    return false;
                }

                System.Windows.Clipboard.SetText(path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Warn(ex, "Failed to copy support log path.");
                return false;
            }
        }

        public DisplayManagerSettings Settings => settings;

        internal event EventHandler DisplaysChanged;

        public void NotifyDisplaysChanged()
        {
            resolutions?.ClearCache();
            refreshRates?.ClearCache();
            Theme?.Refresh();
            RefreshTopPanelItem();
            DisplaysChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Loc(string key)
        {
            var value = PlayniteApi.Resources.GetString(key);
            if (!string.IsNullOrWhiteSpace(value) && value != key)
            {
                return value;
            }

            return GetEnglishFallbackString(key) ?? key;
        }

        public void ReloadSettings()
        {
            settings = new DisplayManagerSettings(this);
            Theme?.Refresh();
            RefreshTopPanelItem();
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new DisplayManagerSettingsView(openingStandaloneSettings);
        }

        private bool OpenStandaloneSettingsView()
        {
            openingStandaloneSettings = true;
            try
            {
                return OpenSettingsView();
            }
            finally
            {
                openingStandaloneSettings = false;
            }
        }

        public bool OpenStandaloneSettingsForTheme()
        {
            return OpenStandaloneSettingsView();
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            if (args == null)
            {
                return null;
            }

            if (string.Equals(args.Name, "DisplayList", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args.Name, "DisplayManager_DisplayList", StringComparison.OrdinalIgnoreCase))
            {
                return new DisplayManagerDisplayListControl(this);
            }

            if (string.Equals(args.Name, "HdrStatus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args.Name, "DisplayManager_HdrStatus", StringComparison.OrdinalIgnoreCase))
            {
                return new DisplayManagerHdrStatusControl(this);
            }

            if (string.Equals(args.Name, "ActiveProfile", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args.Name, "DisplayManager_ActiveProfile", StringComparison.OrdinalIgnoreCase))
            {
                return new DisplayManagerActiveProfileControl(this);
            }

            if (string.Equals(args.Name, "SessionStatus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args.Name, "DisplayManager_SessionStatus", StringComparison.OrdinalIgnoreCase))
            {
                return new DisplayManagerSessionStatusControl(this);
            }

            if (string.Equals(args.Name, "DisplaysSummary", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args.Name, "DisplayManager_DisplaysSummary", StringComparison.OrdinalIgnoreCase))
            {
                return new DisplayManagerDisplaysSummaryControl(this);
            }

            if (string.Equals(args.Name, "OpenSettingsButton", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args.Name, "DisplayManager_OpenSettingsButton", StringComparison.OrdinalIgnoreCase))
            {
                return new DisplayManagerOpenSettingsButtonControl(this);
            }

            return null;
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            if (desktopTopPanelItem == null)
            {
                desktopTopPanelItem = new TopPanelItem
                {
                    Icon = new DisplayManagerTopPanelControl(this),
                    Activated = () => OpenStandaloneSettingsView()
                };
            }

            RefreshTopPanelItem();
            yield return desktopTopPanelItem;
        }

        private void RefreshTopPanelItem()
        {
            if (desktopTopPanelItem == null)
            {
                return;
            }

            desktopTopPanelItem.Visible = settings == null || settings.ShowDesktopTopPanel;
            desktopTopPanelItem.Title = Theme?.TopPanelTooltip
                ?? Loc("LOCDisplayManager_PluginName");
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = Loc("LOCDisplayManager_OpenSettings"),
                MenuSection = "@Display Manager",
                Action = _ => OpenStandaloneSettingsView()
            };
            yield return new MainMenuItem
            {
                Description = Loc("LOCDisplayManager_OpenSetupWizard"),
                MenuSection = "@Display Manager",
                Action = _ => OpenSetupWizard()
            };
            yield return new MainMenuItem
            {
                Description = Loc("LOCDisplayManager_HdrWriteOffNow"),
                MenuSection = "@Display Manager",
                Action = _ =>
                {
                    if (TryWriteHdrOffNow(out var error))
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            Loc("LOCDisplayManager_HdrWriteOffOk"),
                            Loc("LOCDisplayManager_PluginName"));
                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage(
                            error ?? "HDR write failed.",
                            Loc("LOCDisplayManager_PluginName"));
                    }
                }
            };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("Display Manager application started (verbose=" +
                        (settings?.EnableVerboseLogging == true) + ").");
            TryOfferFirstRunSetupWizard();
            BeginModeSession();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            EndGameSession("app-stopped");
            EndModeSession("app-stopped");
            StopRestoreHeartbeat();
            if (restoreClient != null)
            {
                try { restoreClient.Disarm(); } catch { /* ignore */ }
                restoreClient.Dispose();
                restoreClient = null;
            }

            logger.Info("Display Manager application stopped.");
        }

        public void OpenSetupWizard()
        {
            if (PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Desktop)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    Loc("LOCDisplayManager_SetupWizardDesktopOnly"),
                    Loc("LOCDisplayManager_SetupWizardTitle"));
                return;
            }

            if (settings == null)
            {
                return;
            }

            var defaults = settings.GetDefaultDisplayProfile();
            var draft = new SetupWizardDraft
            {
                PreferredPlayDisplayId = defaults?.PreferredPlayDisplayId ?? settings.PreferredPlayDisplayId,
                TurnOffOtherDisplays = defaults?.TurnOffOtherDisplays ?? settings.TurnOffOtherDisplaysOnLaunch,
                GlobalHdrPolicy = settings.GlobalHdrPolicy,
                GlobalRefreshRatePolicy = settings.GlobalRefreshRatePolicy,
                PreferredRefreshRateHz = settings.PreferredRefreshRateHz,
                ClearNativeHdrFlags = true,
                SetupWizardCompleted = settings.SetupWizardCompleted
            };
            var window = new SetupWizardWindow(this, draft, nativeHdrMigration.CountEnabled());
            var owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            if (owner != null)
            {
                window.Owner = owner;
            }

            SettingsAppearance.ApplyWindow(window, settings.AppearancePreset);
            var result = window.ShowDialog();
            if (result == true)
            {
                ApplyWizardDraft(draft);
                PlayniteApi.Dialogs.ShowMessage(
                    Loc("LOCDisplayManager_SetupWizardSaved"),
                    Loc("LOCDisplayManager_SetupWizardTitle"));
                return;
            }

            settings.SetupWizardCompleted = true;
            SavePluginSettings(settings);
        }

        private void TryOfferFirstRunSetupWizard()
        {
            try
            {
                if (settings == null || settings.SetupWizardCompleted)
                {
                    return;
                }

                if (PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Desktop)
                {
                    return;
                }

                Application.Current?.Dispatcher?.BeginInvoke(
                    new Action(OpenSetupWizard),
                    DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to offer the first-run setup wizard.");
            }
        }

        private void ApplyWizardDraft(SetupWizardDraft draft)
        {
            if (draft == null || settings == null)
            {
                return;
            }

            settings.SetupWizardCompleted = true;
            settings.GlobalHdrPolicy = draft.GlobalHdrPolicy;
            settings.GlobalHdrPolicyFullscreen = draft.GlobalHdrPolicy;
            settings.GlobalRefreshRatePolicy = draft.GlobalRefreshRatePolicy;
            settings.GlobalRefreshRatePolicyFullscreen = draft.GlobalRefreshRatePolicy;
            settings.PreferredRefreshRateHz = draft.PreferredRefreshRateHz;
            settings.PreferredRefreshRateHzFullscreen = draft.PreferredRefreshRateHz;

            // First-run: same play display / turn-off for Desktop and Fullscreen.
            // Users can split them later in Settings → Displays.
            foreach (var mode in new[] { ApplicationMode.Desktop, ApplicationMode.Fullscreen })
            {
                var topology = settings.GetDefaultDisplayProfileForMode(mode);
                if (topology == null)
                {
                    continue;
                }

                topology.PreferredPlayDisplayId = draft.PreferredPlayDisplayId;
                topology.TurnOffOtherDisplays = draft.TurnOffOtherDisplays;
            }

            settings.SyncLegacyFieldsFromDefaultTopology();

            if (draft.ClearNativeHdrFlags)
            {
                var migration = nativeHdrMigration.ClearAllEnabled();
                settings.NativeHdrMigrationCompleted = true;
                if (!migration.Success)
                {
                    logger.Warn("Wizard native HDR migration failed: " + migration.Error);
                }
            }

            SavePluginSettings(settings);
            NotifyDisplaysChanged();
            Theme?.Refresh();
            RefreshTopPanelItem();
            logger.Info("Setup wizard finished (HDR policy=" + settings.GlobalHdrPolicy +
                        ", refresh=" + settings.GlobalRefreshRatePolicy +
                        ", clearNativeHdr=" + draft.ClearNativeHdrFlags + ").");
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var games = args?.Games?.ToList();
            if (games == null || games.Count == 0)
            {
                yield break;
            }

            // Heal stale Exact / ExactHz / ForceOn left over after a play-display switch.
            foreach (var game in games)
            {
                SanitizeGameProfileForCurrentPlayDisplay(game);
            }

            var hdrSection = "Display Manager|" + Loc("LOCDisplayManager_GameMenuHdrSection");
            var hdrCurrent = games.Select(g => gameProfiles.GetHdrOverride(g)).ToList();

            yield return CreateHdrOverrideMenuItem(
                hdrSection,
                CheckedMenuLabel(hdrCurrent.All(o => o == GameHdrOverride.Inherit),
                    Loc("LOCDisplayManager_GameHdrInherit")),
                args,
                GameHdrOverride.Inherit);

            yield return CreateHdrOverrideMenuItem(
                hdrSection,
                CheckedMenuLabel(hdrCurrent.All(o => o == GameHdrOverride.ForceOn),
                    Loc("LOCDisplayManager_GameHdrForceOn")),
                args,
                GameHdrOverride.ForceOn);

            yield return CreateHdrOverrideMenuItem(
                hdrSection,
                CheckedMenuLabel(hdrCurrent.All(o => o == GameHdrOverride.ForceOff),
                    Loc("LOCDisplayManager_GameHdrForceOff")),
                args,
                GameHdrOverride.ForceOff);

            var hzSection = "Display Manager|" + Loc("LOCDisplayManager_GameMenuHzSection");
            var hzProfiles = games.Select(g => gameProfiles.GetProfile(g)).ToList();
            var hzCurrent = hzProfiles
                .Select(p => p?.RefreshRateOverride ?? GameRefreshRateOverride.Inherit)
                .ToList();

            yield return CreateRefreshOverrideMenuItem(
                hzSection,
                CheckedMenuLabel(hzCurrent.All(o => o == GameRefreshRateOverride.Inherit),
                    Loc("LOCDisplayManager_GameHzInherit")),
                args,
                GameRefreshRateOverride.Inherit);

            yield return CreateRefreshOverrideMenuItem(
                hzSection,
                CheckedMenuLabel(hzCurrent.All(o => o == GameRefreshRateOverride.Native),
                    Loc("LOCDisplayManager_GameHzNative")),
                args,
                GameRefreshRateOverride.Native);

            var targetDisplay = ResolveMenuTargetDisplay(games);
            var availableRates = RefreshRates?.GetAvailableRates(targetDisplay) ?? Array.Empty<double>();
            foreach (var rate in availableRates)
            {
                var selected = hzProfiles.Count > 0 && hzProfiles.All(p =>
                    p != null
                    && (p.RefreshRateOverride == GameRefreshRateOverride.ExactHz
                        || p.RefreshRateOverride == GameRefreshRateOverride.Prefer60
                        || p.RefreshRateOverride == GameRefreshRateOverride.Prefer120)
                    && p.PreferredRefreshRateHz.HasValue
                    && Math.Abs(p.PreferredRefreshRateHz.Value - rate) < 0.05);
                var format = Loc("LOCDisplayManager_RefreshPolicyExactFormat");
                yield return CreateRefreshOverrideMenuItem(
                    hzSection,
                    CheckedMenuLabel(selected, string.Format(format, rate)),
                    args,
                    GameRefreshRateOverride.ExactHz,
                    rate);
            }

            yield return CreateRefreshOverrideMenuItem(
                hzSection,
                CheckedMenuLabel(hzCurrent.All(o => o == GameRefreshRateOverride.HighestDetected),
                    Loc("LOCDisplayManager_GameHzHighest")),
                args,
                GameRefreshRateOverride.HighestDetected);

            var resolutionSection = "Display Manager|" + Loc("LOCDisplayManager_GameMenuResolutionSection");
            var resolutionProfiles = games.Select(g => gameProfiles.GetProfile(g)).ToList();
            var resolutionCurrent = resolutionProfiles
                .Select(p => p?.ResolutionOverride ?? GameResolutionOverride.Inherit)
                .ToList();
            yield return CreateResolutionOverrideMenuItem(
                resolutionSection,
                CheckedMenuLabel(resolutionCurrent.All(o => o == GameResolutionOverride.Inherit),
                    Loc("LOCDisplayManager_GameResolutionInherit")),
                args,
                GameResolutionOverride.Inherit);
            yield return CreateResolutionOverrideMenuItem(
                resolutionSection,
                CheckedMenuLabel(resolutionCurrent.All(o => o == GameResolutionOverride.Native),
                    Loc("LOCDisplayManager_GameResolutionNative")),
                args,
                GameResolutionOverride.Native);

            var availableModes = Resolutions?.GetAvailableModes(targetDisplay) ?? new List<ResolutionMode>();
            var customSuffix = Loc("LOCDisplayManager_ResolutionCustomSuffix");
            foreach (var mode in availableModes)
            {
                var selected = resolutionProfiles.Count > 0 && resolutionProfiles.All(p =>
                    p != null
                    && p.ResolutionOverride == GameResolutionOverride.Exact
                    && p.PreferredResolutionWidth == mode.Width
                    && p.PreferredResolutionHeight == mode.Height);
                var label = mode.IsCustom && !string.IsNullOrWhiteSpace(customSuffix)
                    ? mode.Label + " (" + customSuffix + ")"
                    : mode.Label;
                yield return CreateResolutionOverrideMenuItem(
                    resolutionSection,
                    CheckedMenuLabel(selected, label),
                    args,
                    GameResolutionOverride.Exact,
                    mode.Width,
                    mode.Height);
            }

            yield return CreateResolutionOverrideMenuItem(
                resolutionSection,
                CheckedMenuLabel(resolutionCurrent.All(o => o == GameResolutionOverride.LowestAvailable),
                    Loc("LOCDisplayManager_GameResolutionLowest")),
                args,
                GameResolutionOverride.LowestAvailable);
            yield return CreateResolutionOverrideMenuItem(
                resolutionSection,
                CheckedMenuLabel(resolutionCurrent.All(o => o == GameResolutionOverride.HighestAvailable),
                    Loc("LOCDisplayManager_GameResolutionHighest")),
                args,
                GameResolutionOverride.HighestAvailable);

            var displaySection = "Display Manager|" + Loc("LOCDisplayManager_GameMenuDisplaySection");
            var displayProfiles = games.Select(g => gameProfiles.GetProfile(g)).ToList();
            var inheritDisplay = displayProfiles.Count > 0
                && displayProfiles.All(p => p == null || !p.HasPlayDisplayOverride);
            yield return CreatePlayDisplayMenuItem(
                displaySection,
                CheckedMenuLabel(inheritDisplay, Loc("LOCDisplayManager_GameDisplayInherit")),
                args,
                null);

            var windowsDisplay = displayProfiles.Count > 0
                && displayProfiles.All(p => p != null
                    && p.HasPlayDisplayOverride
                    && string.IsNullOrEmpty(p.PreferredPlayDisplayId));
            yield return CreatePlayDisplayMenuItem(
                displaySection,
                CheckedMenuLabel(windowsDisplay, Loc("LOCDisplayManager_PlayDisplayWindowsDefault")),
                args,
                string.Empty);

            foreach (var display in Displays.GetVisibleDisplays(settings?.DisplayAliases)
                .Where(d => d.IsConnected))
            {
                var displayId = display.Id;
                var selected = displayProfiles.Count > 0 && displayProfiles.All(p =>
                    p != null
                    && p.HasPlayDisplayOverride
                    && string.Equals(p.PreferredPlayDisplayId, displayId, StringComparison.OrdinalIgnoreCase));
                yield return CreatePlayDisplayMenuItem(
                    displaySection,
                    CheckedMenuLabel(selected, display.EffectiveName),
                    args,
                    displayId);
            }

            var turnOffSection = "Display Manager|" + Loc("LOCDisplayManager_GameMenuTurnOffSection");
            var turnOffCurrent = displayProfiles
                .Select(p => p?.TurnOffOtherDisplaysOverride)
                .ToList();
            yield return CreateTurnOffOthersMenuItem(
                turnOffSection,
                CheckedMenuLabel(turnOffCurrent.All(o => o == null),
                    Loc("LOCDisplayManager_GameTurnOffInherit")),
                args,
                null);
            yield return CreateTurnOffOthersMenuItem(
                turnOffSection,
                CheckedMenuLabel(turnOffCurrent.Count > 0 && turnOffCurrent.All(o => o == true),
                    Loc("LOCDisplayManager_GameTurnOffOn")),
                args,
                true);
            yield return CreateTurnOffOthersMenuItem(
                turnOffSection,
                CheckedMenuLabel(turnOffCurrent.Count > 0 && turnOffCurrent.All(o => o == false),
                    Loc("LOCDisplayManager_GameTurnOffOff")),
                args,
                false);
        }

        /// <summary>
        /// Resolves Game > Platform > mode launch default display profile + globals.
        /// Desktop and Fullscreen can use different launch defaults.
        /// </summary>
        public ResolvedSessionProfile ResolveSessionProfile(Game game)
        {
            var gameProfile = gameProfiles?.GetProfile(game);
            var platformProfile = GetPlatformProfileForGame(game);
            var mode = PlayniteApi.ApplicationInfo.Mode;
            var defaultTopology = settings?.GetDefaultDisplayProfileForMode(mode);
            return SessionProfileResolver.Resolve(
                gameProfile,
                platformProfile,
                defaultTopology,
                id => settings?.GetDisplayProfile(id));
        }

        private GameDisplayProfile GetPlatformProfileForGame(Game game)
        {
            if (game?.PlatformIds == null || platformProfiles == null)
            {
                return null;
            }

            foreach (var platformId in game.PlatformIds)
            {
                var profile = platformProfiles.GetProfile(platformId);
                if (profile != null && !profile.IsEmpty)
                {
                    return profile;
                }
            }

            return null;
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            BeginGameSession(args?.Game);
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            EndGameSession("game-stopped");
        }

        public override void OnGameStartupCancelled(OnGameStartupCancelledEventArgs args)
        {
            EndGameSession("startup-cancelled");
        }

        private void BeginModeSession()
        {
            // Always apply the Fullscreen primary (and that mode's topology) when entering
            // Playnite Fullscreen; keep it for the mode session so games do not thrash.
            if (PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Fullscreen)
            {
                return;
            }

            if (IsModeSessionActive)
            {
                return;
            }

            try
            {
                var resolved = ResolveSessionProfile(null);
                var livePreview = Displays.GetDisplays().ToList();
                var currentPrimary = livePreview.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                    ?? livePreview.FirstOrDefault(d => d.IsConnected);

                var preferredId = resolved.PreferredPlayDisplayId;
                var preferredExists = !string.IsNullOrWhiteSpace(preferredId)
                    && livePreview.Any(d => d.IsConnected
                        && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));

                if (!preferredExists && !string.IsNullOrWhiteSpace(preferredId))
                {
                    preferredExists = WaitForPreferredDisplay(preferredId, out livePreview);
                    currentPrimary = livePreview.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                        ?? livePreview.FirstOrDefault(d => d.IsConnected);
                }

                var missingPreferred = !string.IsNullOrWhiteSpace(preferredId) && !preferredExists;
                if (missingPreferred)
                {
                    NotifyMissingDisplay(null, preferredId, resolved);
                    preferredId = ResolveMissingDisplayFallback(resolved, livePreview, currentPrimary);
                    preferredExists = !string.IsNullOrWhiteSpace(preferredId)
                        && livePreview.Any(d => d.IsConnected
                            && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
                }

                var wantsMakePrimary = preferredExists
                    && (currentPrimary == null
                        || !string.Equals(currentPrimary.Id, preferredId, StringComparison.OrdinalIgnoreCase));
                var topologyTargetId = preferredExists
                    ? preferredId
                    : currentPrimary?.Id;
                var wantsTurnOffOthers = resolved.TurnOffOtherDisplays
                    && !string.IsNullOrWhiteSpace(topologyTargetId);
                var wantsTopology = wantsMakePrimary || wantsTurnOffOthers;
                if (!wantsTopology)
                {
                    fileLogger.InfoTopic(
                        "mode.skip",
                        "topology=none preferred=" + (preferredId ?? "(windows-primary)"));
                    return;
                }

                var topologyPlan =
                    (wantsMakePrimary ? "makePrimary" : string.Empty) +
                    (wantsMakePrimary && wantsTurnOffOthers ? "+" : string.Empty) +
                    (wantsTurnOffOthers ? "turnOffOthers" : string.Empty);

                var snapshot = Topology.CaptureSnapshot();
                modeSessionSnapshot = snapshot;
                Theme?.Refresh();

                EnsureRestoreClient();
                restoreClient.Arm(snapshot);
                StartRestoreHeartbeat();
                fileLogger.InfoTopic(
                    "mode.begin",
                    "preferred=" + (topologyTargetId ?? "(windows-primary)") +
                    " missingPreferred=" + missingPreferred +
                    " topology=" + topologyPlan);

                var topologyApply = Topology.TryApplyRequest(new DisplayTopologyRequest
                {
                    TargetDisplayId = topologyTargetId,
                    MakePrimary = wantsMakePrimary,
                    TurnOffOtherDisplays = wantsTurnOffOthers
                });
                if (!topologyApply.Success)
                {
                    fileLogger.WarnTopic(
                        "mode.topology",
                        "failed target=" + (topologyTargetId ?? string.Empty) +
                        " plan=" + topologyPlan,
                        topologyApply.Error);
                    modeSessionSnapshot = null;
                    DisarmRestoreLease();
                    Theme?.Refresh();
                    return;
                }

                fileLogger.InfoTopic(
                    "mode.topology",
                    "ok target=" + (topologyTargetId ?? string.Empty) +
                    " plan=" + topologyPlan +
                    " message=" + (topologyApply.Message ?? string.Empty));
                SettleAfterDisplayChange(true);
                NotifyDisplaysChanged();
                Theme?.Refresh();
            }
            catch (Exception ex)
            {
                fileLogger.ErrorTopic("mode.begin", "Failed to begin Fullscreen mode display session.", ex);
                modeSessionSnapshot = null;
            }
        }

        private void EndModeSession(string reason)
        {
            if (modeSessionSnapshot == null)
            {
                return;
            }

            try
            {
                var snapshot = modeSessionSnapshot;
                modeSessionSnapshot = null;
                Theme?.Refresh();

                if (!Topology.TryRestoreSnapshot(snapshot, out var error))
                {
                    fileLogger.WarnTopic(
                        "mode.restore",
                        "failed reason=" + reason,
                        error);
                }
                else
                {
                    fileLogger.InfoTopic("mode.restore", "ok reason=" + reason);
                }

                NotifyDisplaysChanged();
            }
            catch (Exception ex)
            {
                fileLogger.ErrorTopic(
                    "mode.end",
                    "Failed to end Fullscreen mode display session (" + reason + ").",
                    ex);
            }
        }

        private void BeginGameSession(Game game)
        {
            if (game == null || settings == null)
            {
                return;
            }

            // NX owns HDR: never stack with Playnite's native EnableSystemHdr restore.
            ClearNativeHdrConflictIfNeeded(game);

            var resolved = ResolveSessionProfile(game);
            var hdrPlan = PlanHdrSession(game, resolved);

            var livePreview = Displays.GetDisplays().ToList();
            var currentPrimary = livePreview.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? livePreview.FirstOrDefault(d => d.IsConnected);

            var preferredId = resolved.PreferredPlayDisplayId;
            var preferredExists = !string.IsNullOrWhiteSpace(preferredId)
                && livePreview.Any(d => d.IsConnected
                    && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));

            if (!preferredExists && !string.IsNullOrWhiteSpace(preferredId))
            {
                preferredExists = WaitForPreferredDisplay(preferredId, out livePreview);
                currentPrimary = livePreview.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                    ?? livePreview.FirstOrDefault(d => d.IsConnected);
            }

            var missingPreferred = !string.IsNullOrWhiteSpace(preferredId) && !preferredExists;
            if (missingPreferred)
            {
                NotifyMissingDisplay(game, preferredId, resolved);
                preferredId = ResolveMissingDisplayFallback(resolved, livePreview, currentPrimary);
                preferredExists = !string.IsNullOrWhiteSpace(preferredId)
                    && livePreview.Any(d => d.IsConnected
                        && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            }

            var wantsMakePrimary = preferredExists
                && (currentPrimary == null
                    || !string.Equals(currentPrimary.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            var topologyTargetId = preferredExists
                ? preferredId
                : currentPrimary?.Id;
            var modeOwnsTopology = IsModeSessionActive;
            var wantsTurnOffOthers = !modeOwnsTopology
                && resolved.TurnOffOtherDisplays
                && !string.IsNullOrWhiteSpace(topologyTargetId);
            if (modeOwnsTopology)
            {
                wantsMakePrimary = false;
            }

            var wantsTopology = wantsMakePrimary || wantsTurnOffOthers;
            var plannedTarget = !string.IsNullOrWhiteSpace(topologyTargetId)
                ? livePreview.FirstOrDefault(d => string.Equals(d.Id, topologyTargetId, StringComparison.OrdinalIgnoreCase))
                : currentPrimary;
            var resolutionPlan = PlanResolutionSession(game, resolved, plannedTarget);
            var hzPlan = PlanRefreshRateSession(game, resolved, plannedTarget);

            if (hdrPlan.Action == HdrSessionAction.None
                && !resolutionPlan.ShouldApply
                && !hzPlan.ShouldApply
                && !wantsTopology)
            {
                fileLogger.InfoTopic(
                    "session.skip",
                    "game=" + (game.Name ?? string.Empty) +
                    " id=" + PluginFileLogger.ShortId(game.Id) +
                    " source=" + resolved.Source +
                    " hdr=" + hdrPlan.Reason +
                    " resolution=" + resolutionPlan.Reason +
                    " hz=" + hzPlan.Reason +
                    " topology=none");
                return;
            }

            try
            {
                EndGameSession("replace-session");
                fileLogger.SessionBegin(game.Id, game.Name);

                var live = Displays.GetDisplays().ToList();
                var primary = live.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                    ?? live.FirstOrDefault(d => d.IsConnected);

                List<HdrWriteTarget> applyWrites = new List<HdrWriteTarget>();
                List<HdrWriteTarget> offWrites = new List<HdrWriteTarget>();
                if (hdrPlan.Action == HdrSessionAction.Enable)
                {
                    applyWrites = hdr.BuildEnableWritesForPrimary(live);
                    offWrites = applyWrites.Select(ToOffWrite).ToList();
                }
                else if (hdrPlan.Action == HdrSessionAction.Disable)
                {
                    applyWrites = hdr.BuildForceOffWrites(live);
                    offWrites = applyWrites.Select(ToOffWrite).ToList();
                }

                if (offWrites.Count == 0 && hdrPlan.Action != HdrSessionAction.None)
                {
                    offWrites = hdr.BuildForceOffWrites(live);
                }

                var topologyPlan =
                    (wantsMakePrimary ? "makePrimary" : string.Empty) +
                    (wantsMakePrimary && wantsTurnOffOthers ? "+" : string.Empty) +
                    (wantsTurnOffOthers ? "turnOffOthers" : string.Empty);
                if (string.IsNullOrEmpty(topologyPlan))
                {
                    topologyPlan = "none";
                }

                fileLogger.InfoTopic(
                    "session.plan",
                    "source=" + resolved.Source +
                    " preferred=" + (topologyTargetId ?? "(windows-primary)") +
                    " missingPreferred=" + missingPreferred +
                    " topology=" + topologyPlan +
                    " resolution=" + (resolutionPlan.ShouldApply
                        ? resolutionPlan.TargetWidth + "x" + resolutionPlan.TargetHeight
                        : "skip") +
                    " (" + resolutionPlan.Reason + ")" +
                    " hz=" + (hzPlan.ShouldApply
                        ? hzPlan.TargetHz.Value.ToString("0.###")
                        : "skip") +
                    " (" + hzPlan.Reason + ")" +
                    " hdr=" + hdrPlan.Action + " (" + hdrPlan.Reason + ")" +
                    " applyWrites=" + applyWrites.Count +
                    " restoreOffWrites=" + offWrites.Count,
                    FormatHdrWritesDetail(applyWrites, offWrites));

                var snapshot = Topology.CaptureSnapshot();
                snapshot.HdrRestoreWrites = offWrites;
                gameSessionSnapshot = snapshot;
                activeGameId = game.Id;
                activeGameName = game.Name;
                Theme?.Refresh();

                EnsureRestoreClient();
                if (modeOwnsTopology)
                {
                    // Keep the Fullscreen mode lease (desktop layout) armed for crash restore.
                    restoreClient.Arm(modeSessionSnapshot);
                    StartRestoreHeartbeat();
                    fileLogger.InfoTopic(
                        "restore.arm",
                        "mode-lease kept for game session snapshotTargets=" +
                        (snapshot.HdrRestoreWrites == null ? 0 : snapshot.HdrRestoreWrites.Count));
                }
                else
                {
                    restoreClient.Arm(snapshot);
                    StartRestoreHeartbeat();
                    fileLogger.InfoTopic("restore.arm", "lease armed snapshotTargets=" +
                        (snapshot.HdrRestoreWrites == null ? 0 : snapshot.HdrRestoreWrites.Count));
                }
                var appliedAny = false;

                if (wantsTopology)
                {
                    var topologyApply = Topology.TryApplyRequest(new DisplayTopologyRequest
                    {
                        TargetDisplayId = topologyTargetId,
                        MakePrimary = wantsMakePrimary,
                        TurnOffOtherDisplays = wantsTurnOffOthers
                    });
                    if (!topologyApply.Success)
                    {
                        fileLogger.WarnTopic(
                            "topology.apply",
                            "failed target=" + (topologyTargetId ?? string.Empty) +
                            " plan=" + topologyPlan,
                            topologyApply.Error);
                    }
                    else
                    {
                        appliedAny = true;
                        fileLogger.InfoTopic(
                            "topology.apply",
                            "ok target=" + (topologyTargetId ?? string.Empty) +
                            " plan=" + topologyPlan +
                            " message=" + (topologyApply.Message ?? string.Empty));
                        live = Displays.GetDisplays().ToList();
                        primary = live.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                            ?? live.FirstOrDefault(d => d.IsConnected);
                        if (hdrPlan.Action == HdrSessionAction.Enable)
                        {
                            applyWrites = hdr.BuildEnableWritesForPrimary(live);
                            offWrites = applyWrites.Select(ToOffWrite).ToList();
                            snapshot.HdrRestoreWrites = offWrites;
                        }
                    }
                }

                resolutionPlan = PlanResolutionSession(game, resolved, primary);
                if (resolutionPlan.ShouldApply
                    && resolutionPlan.TargetWidth.HasValue
                    && resolutionPlan.TargetHeight.HasValue
                    && primary != null)
                {
                    if (resolutions.TryApply(primary, resolutionPlan.TargetWidth.Value, resolutionPlan.TargetHeight.Value, out var resolutionError))
                    {
                        appliedAny = true;
                        fileLogger.InfoTopic(
                            "resolution.apply",
                            "ok " + resolutionPlan.TargetWidth.Value + "x" + resolutionPlan.TargetHeight.Value +
                            " display=" + (primary.Id ?? string.Empty) +
                            " (" + resolutionPlan.Reason + ")");
                        live = Displays.GetDisplays().ToList();
                        primary = live.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                            ?? live.FirstOrDefault(d => d.IsConnected);
                    }
                    else
                    {
                        fileLogger.WarnTopic(
                            "resolution.apply",
                            "failed " + resolutionPlan.TargetWidth.Value + "x" + resolutionPlan.TargetHeight.Value +
                            " (" + resolutionPlan.Reason + ")",
                            resolutionError);
                    }
                }

                hzPlan = PlanRefreshRateSession(game, resolved, primary);
                if (hzPlan.ShouldApply && hzPlan.TargetHz.HasValue && primary != null)
                {
                    if (refreshRates.TryApply(primary, hzPlan.TargetHz.Value, out var hzError))
                    {
                        appliedAny = true;
                        fileLogger.InfoTopic(
                            "hz.apply",
                            "ok " + hzPlan.TargetHz.Value.ToString("0.###") + " Hz" +
                            " display=" + (primary.Id ?? string.Empty) +
                            " (" + hzPlan.Reason + ")");
                    }
                    else
                    {
                        fileLogger.WarnTopic(
                            "hz.apply",
                            "failed " + hzPlan.TargetHz.Value.ToString("0.###") + " Hz" +
                            " (" + hzPlan.Reason + ")",
                            hzError);
                    }
                }

                if (hdrPlan.Action == HdrSessionAction.None)
                {
                    SettleAfterDisplayChange(appliedAny);
                    return;
                }

                if (applyWrites.Count == 0)
                {
                    fileLogger.InfoTopic(
                        "hdr.apply",
                        "skip action=" + hdrPlan.Action +
                        " (" + hdrPlan.Reason + "): no advanced-color-capable target; lease armed");
                    SettleAfterDisplayChange(appliedAny);
                    return;
                }

                var written = hdr.ApplyHdrWrites(applyWrites, out var error);
                if (written == 0)
                {
                    fileLogger.WarnTopic(
                        "hdr.apply",
                        "failed action=" + hdrPlan.Action + " (" + hdrPlan.Reason + ") wrote=0",
                        error);
                }
                else
                {
                    appliedAny = true;
                    fileLogger.InfoTopic(
                        "hdr.apply",
                        "ok action=" + hdrPlan.Action +
                        " wrote=" + written +
                        " (" + hdrPlan.Reason + ")",
                        FormatHdrWritesDetail(applyWrites, null));
                }

                SettleAfterDisplayChange(appliedAny);
            }
            catch (Exception ex)
            {
                fileLogger.ErrorTopic("session.begin", "Failed to begin Display Manager game session.", ex);
                if (gameSessionSnapshot == null && activeGameId == null)
                {
                    fileLogger.SessionEnd(false, "begin-failed");
                }
            }
        }

        private static string FormatHdrWritesDetail(
            IList<HdrWriteTarget> applyWrites,
            IList<HdrWriteTarget> offWrites)
        {
            var sb = new System.Text.StringBuilder();
            if (applyWrites != null && applyWrites.Count > 0)
            {
                sb.Append("applyWrites:");
                sb.Append(Environment.NewLine);
                foreach (var w in applyWrites)
                {
                    sb.Append("  enable=").Append(w.Enable)
                        .Append(" targetId=").Append(w.TargetId)
                        .Append(" display=").Append(w.DisplayId ?? string.Empty)
                        .Append(" name=").Append(w.Name ?? string.Empty)
                        .Append(Environment.NewLine);
                }
            }

            if (offWrites != null && offWrites.Count > 0)
            {
                sb.Append("restoreOffWrites:");
                sb.Append(Environment.NewLine);
                foreach (var w in offWrites)
                {
                    sb.Append("  enable=").Append(w.Enable)
                        .Append(" targetId=").Append(w.TargetId)
                        .Append(" display=").Append(w.DisplayId ?? string.Empty)
                        .Append(" name=").Append(w.Name ?? string.Empty)
                        .Append(Environment.NewLine);
                }
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        private static HdrWriteTarget ToOffWrite(HdrWriteTarget w)
        {
            return new HdrWriteTarget
            {
                AdapterIdLow = w.AdapterIdLow,
                AdapterIdHigh = w.AdapterIdHigh,
                TargetId = w.TargetId,
                Enable = false,
                DisplayId = w.DisplayId,
                Name = w.Name
            };
        }

        private void EndGameSession(string reason)
        {
            if (gameSessionSnapshot == null && activeGameId == null)
            {
                return;
            }

            try
            {
                var snapshot = gameSessionSnapshot;
                gameSessionSnapshot = null;
                activeGameId = null;
                var name = activeGameName;
                activeGameName = null;
                Theme?.Refresh();

                if (snapshot != null)
                {
                    var restoreOk = false;
                    // Restore writes HDR off from snapshot.HdrRestoreWrites — no GET trust.
                    if (!Topology.TryRestoreSnapshot(snapshot, out var error))
                    {
                        fileLogger.WarnTopic(
                            "session.restore",
                            "failed reason=" + reason +
                            (string.IsNullOrWhiteSpace(name) ? string.Empty : " game=" + name),
                            error);
                        if (snapshot.HdrRestoreWrites != null && snapshot.HdrRestoreWrites.Count > 0)
                        {
                            hdr.ApplyHdrWrites(snapshot.HdrRestoreWrites, out _);
                        }
                    }
                    else
                    {
                        restoreOk = true;
                        fileLogger.InfoTopic(
                            "session.restore",
                            "ok reason=" + reason +
                            (string.IsNullOrWhiteSpace(name) ? string.Empty : " game=" + name));
                    }

                    if (restoreOk
                        && !string.Equals(reason, "replace-session", StringComparison.Ordinal)
                        && settings?.RelocatePlayniteFullscreenAfterRestore == true
                        && PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                    {
                        PlayniteWindowRelocator.TryRelocateToPrimaryMonitor(logger);
                    }

                    fileLogger.SessionEnd(restoreOk, reason);
                }
                else
                {
                    fileLogger.SessionEnd(true, reason);
                }

                if (IsModeSessionActive)
                {
                    EnsureRestoreClient();
                    restoreClient.Arm(modeSessionSnapshot);
                    StartRestoreHeartbeat();
                    fileLogger.InfoTopic("restore.arm", "mode-lease re-armed after game session");
                }
                else
                {
                    DisarmRestoreLease();
                }

                NotifyDisplaysChanged();
            }
            catch (Exception ex)
            {
                fileLogger.ErrorTopic(
                    "session.end",
                    "Failed to end Display Manager game session (" + reason + ").",
                    ex);
                if (fileLogger.HasOpenSession)
                {
                    fileLogger.SessionEnd(false, reason);
                }
            }
        }

        /// <summary>
        /// Same target as Settings Exact/Hz lists: per-game play display override when shared,
        /// otherwise the global preferred play display, otherwise Windows primary.
        /// </summary>
        private DisplayInfo ResolveMenuTargetDisplay(IList<Game> games)
        {
            var live = Displays.GetVisibleDisplays(settings?.DisplayAliases)
                .Where(d => d.IsConnected)
                .ToList();
            if (live.Count == 0)
            {
                return null;
            }

            string preferredId = null;
            if (games != null && games.Count > 0)
            {
                var playIds = games
                    .Select(g => gameProfiles.GetProfile(g))
                    .Select(p => p != null && p.HasPlayDisplayOverride
                        ? p.PreferredPlayDisplayId
                        : null)
                    .ToList();
                // null = inherit global; empty = keep Windows primary; otherwise display id.
                if (playIds.Count > 0 && playIds.All(id => id != null)
                    && playIds.All(id => string.Equals(id, playIds[0], StringComparison.OrdinalIgnoreCase)))
                {
                    preferredId = playIds[0];
                }
            }

            if (preferredId == null)
            {
                preferredId = settings?.PreferredPlayDisplayId;
            }

            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                var match = live.FirstOrDefault(d =>
                    string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            // Empty override or missing preferred → Windows primary.
            return live.FirstOrDefault(d => d.IsPrimary) ?? live[0];
        }

        private string ResolveMissingDisplayFallback(
            ResolvedSessionProfile resolved,
            IList<DisplayInfo> live,
            DisplayInfo currentPrimary)
        {
            if (resolved == null)
            {
                return currentPrimary?.Id;
            }

            if (resolved.MissingDisplayPolicy == MissingDisplayPolicy.UseFallbackDisplay
                && !string.IsNullOrWhiteSpace(resolved.FallbackDisplayId)
                && live.Any(d => d.IsConnected
                    && string.Equals(d.Id, resolved.FallbackDisplayId, StringComparison.OrdinalIgnoreCase)))
            {
                return resolved.FallbackDisplayId;
            }

            return null; // Windows primary — no MakePrimary
        }

        private void NotifyMissingDisplay(Game game, string missingId, ResolvedSessionProfile resolved)
        {
            if (settings == null || !settings.ShowNotifications)
            {
                return;
            }

            var strong = resolved?.MissingDisplayPolicy == MissingDisplayPolicy.NotifyAndContinue;
            if (!strong && resolved?.MissingDisplayPolicy == MissingDisplayPolicy.UseWindowsPrimary)
            {
                // Still notify lightly when preferred is missing.
            }

            var name = Displays.GetDisplays()
                .FirstOrDefault(d => string.Equals(d.Id, missingId, StringComparison.OrdinalIgnoreCase))
                ?.EffectiveName ?? missingId;
            var text = string.Format(
                Loc("LOCDisplayManager_MissingDisplayNotifyFormat"),
                game?.Name ?? Loc("LOCDisplayManager_FullscreenRelocateTitle"),
                name);
            try
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "DisplayManager-MissingDisplay-" + (game?.Id.ToString() ?? "x"),
                    text,
                    strong ? NotificationType.Error : NotificationType.Info));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to show missing-display notification.");
            }
        }

        public HdrSessionPlan PlanHdrSession(Game game)
        {
            return PlanHdrSession(game, ResolveSessionProfile(game));
        }

        public HdrSessionPlan PlanHdrSession(Game game, ResolvedSessionProfile resolved)
        {
            var synthetic = new GameDisplayProfile
            {
                HdrOverride = resolved?.HdrOverride ?? GameHdrOverride.Inherit
            };
            var mode = PlayniteApi.ApplicationInfo.Mode;
            return HdrSessionPlanner.Plan(
                game,
                settings?.GetHdrPolicyForMode(mode) ?? GlobalHdrPolicy.DoNotManage,
                synthetic,
                settings?.HdrMetadataMatchNames,
                settings?.IncludeTagsInHdrMetadataMatch ?? false);
        }

        public RefreshRatePlan PlanRefreshRateSession(Game game)
        {
            return PlanRefreshRateSession(game, ResolveSessionProfile(game));
        }

        public RefreshRatePlan PlanRefreshRateSession(Game game, ResolvedSessionProfile resolved)
        {
            var primary = Displays.GetDisplays()
                .FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? Displays.GetDisplays().FirstOrDefault(d => d.IsConnected);
            return PlanRefreshRateSession(game, resolved, primary);
        }

        public RefreshRatePlan PlanRefreshRateSession(Game game, ResolvedSessionProfile resolved, DisplayInfo primary)
        {
            var mode = PlayniteApi.ApplicationInfo.Mode;
            var gameOverride = resolved?.RefreshRateOverride ?? GameRefreshRateOverride.Inherit;
            double? preferredHz = settings?.GetPreferredRefreshRateHzForMode(mode);
            if (gameOverride == GameRefreshRateOverride.ExactHz
                || gameOverride == GameRefreshRateOverride.Prefer60
                || gameOverride == GameRefreshRateOverride.Prefer120)
            {
                preferredHz = resolved?.PreferredRefreshRateHz ?? preferredHz;
            }

            return refreshRates.Plan(
                settings?.GetRefreshRatePolicyForMode(mode) ?? RefreshRatePolicy.Native,
                gameOverride,
                primary,
                preferredHz);
        }

        public ResolutionPlan PlanResolutionSession(Game game)
        {
            return PlanResolutionSession(game, ResolveSessionProfile(game));
        }

        public ResolutionPlan PlanResolutionSession(Game game, ResolvedSessionProfile resolved)
        {
            var primary = Displays.GetDisplays()
                .FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? Displays.GetDisplays().FirstOrDefault(d => d.IsConnected);
            return PlanResolutionSession(game, resolved, primary);
        }

        public ResolutionPlan PlanResolutionSession(Game game, ResolvedSessionProfile resolved, DisplayInfo primary)
        {
            var mode = PlayniteApi.ApplicationInfo.Mode;
            var gameOverride = resolved?.ResolutionOverride ?? GameResolutionOverride.Inherit;
            int? preferredWidth = null;
            int? preferredHeight = null;
            settings?.GetPreferredResolutionForMode(mode, out preferredWidth, out preferredHeight);
            if (gameOverride == GameResolutionOverride.Exact)
            {
                preferredWidth = resolved?.PreferredResolutionWidth ?? preferredWidth;
                preferredHeight = resolved?.PreferredResolutionHeight ?? preferredHeight;
            }

            return resolutions.Plan(
                settings?.GetResolutionPolicyForMode(mode) ?? ResolutionPolicy.Native,
                gameOverride,
                primary,
                preferredWidth,
                preferredHeight);
        }

        private void SettleAfterDisplayChange(bool appliedAny)
        {
            var delay = settings?.PostChangeSettleDelayMs ?? 1000;
            if (!appliedAny || delay <= 0)
            {
                return;
            }

            Thread.Sleep(delay);
        }

        /// <summary>
        /// Polls connected displays until the preferred id appears or the configured wait elapses.
        /// </summary>
        private bool WaitForPreferredDisplay(string preferredId, out List<DisplayInfo> livePreview)
        {
            livePreview = Displays.GetDisplays().ToList();
            var waitMs = settings?.PreferredDisplayWaitMs ?? 0;
            if (waitMs <= 0 || string.IsNullOrWhiteSpace(preferredId))
            {
                return livePreview.Any(d => d.IsConnected
                    && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            const int pollMs = 500;
            while (true)
            {
                livePreview = Displays.GetDisplays().ToList();
                if (livePreview.Any(d => d.IsConnected
                    && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase)))
                {
                    logger.Info("Preferred display became available after wait: " + preferredId);
                    return true;
                }

                var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0)
                {
                    break;
                }

                Thread.Sleep(Math.Min(pollMs, remaining));
            }

            livePreview = Displays.GetDisplays().ToList();
            return livePreview.Any(d => d.IsConnected
                && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
        }

        public bool GameHasHdrMetadata(Game game)
        {
            return HdrMetadataMatcher.GameIndicatesHdr(
                game,
                settings?.HdrMetadataMatchNames,
                settings?.IncludeTagsInHdrMetadataMatch ?? false);
        }

        public int CountNativeHdrEnabledGames()
        {
            return nativeHdrMigration?.CountEnabled() ?? 0;
        }

        public int CountNativeHdrBackupIds()
        {
            return nativeHdrMigration?.CountBackupIds() ?? 0;
        }

        public NativeHdrMigrationResult RunNativeHdrMigration()
        {
            var result = nativeHdrMigration.ClearAllEnabled();
            if (result.Success && settings != null)
            {
                settings.NativeHdrMigrationCompleted = true;
                SavePluginSettings(settings);
            }

            NotifyDisplaysChanged();
            return result;
        }

        public NativeHdrMigrationResult RestoreNativeHdrFromBackup()
        {
            var result = nativeHdrMigration.RestoreFromBackup();
            NotifyDisplaysChanged();
            return result;
        }

        public string GetNativeHdrOverviewText()
        {
            var enabled = CountNativeHdrEnabledGames();
            if (enabled <= 0)
            {
                return Loc("LOCDisplayManager_OverviewNativeHdrClear");
            }

            return string.Format(Loc("LOCDisplayManager_OverviewNativeHdrConflictFormat"), enabled);
        }

        public string GetRefreshRateOverviewText()
        {
            var mode = PlayniteApi.ApplicationInfo.Mode;
            switch (settings?.GetRefreshRatePolicyForMode(mode) ?? RefreshRatePolicy.Native)
            {
                case RefreshRatePolicy.ExactHz:
                case RefreshRatePolicy.Prefer60:
                case RefreshRatePolicy.Prefer120:
                    var hz = settings?.GetPreferredRefreshRateHzForMode(mode);
                    if (hz > 0)
                    {
                        return string.Format(
                            Loc("LOCDisplayManager_RefreshPolicyExactFormat"),
                            hz.Value);
                    }
                    return Loc("LOCDisplayManager_RefreshPolicyExact");
                case RefreshRatePolicy.HighestDetected:
                    return Loc("LOCDisplayManager_RefreshPolicyHighest");
                default:
                    return Loc("LOCDisplayManager_RefreshPolicyNative");
            }
        }

        public string GetHdrActionOverviewText()
        {
            var mode = PlayniteApi.ApplicationInfo.Mode;
            switch (settings?.GetHdrPolicyForMode(mode) ?? GlobalHdrPolicy.DoNotManage)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    return Loc("LOCDisplayManager_OverviewActionAlwaysOn");
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    return Loc("LOCDisplayManager_OverviewActionMetadata");
                case GlobalHdrPolicy.UsePlayniteNative:
                    return Loc("LOCDisplayManager_OverviewActionPlayniteNative");
                default:
                    return Loc("LOCDisplayManager_OverviewActionDoNotManage");
            }
        }

        public Game GetSelectedLibraryGame()
        {
            try
            {
                return PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void ClearNativeHdrConflictIfNeeded(Game game)
        {
            if (game == null || nativeHdrMigration == null)
            {
                return;
            }

            // Defer to Playnite's EnableSystemHdr — do not strip the native flag.
            if (settings?.GetHdrPolicyForMode(ApplicationMode.Desktop) == GlobalHdrPolicy.UsePlayniteNative
                || settings?.GetHdrPolicyForMode(ApplicationMode.Fullscreen) == GlobalHdrPolicy.UsePlayniteNative)
            {
                return;
            }

            if (!nativeHdrMigration.TryClearGame(game))
            {
                return;
            }

            if (settings == null || settings.NativeHdrConflictNotified)
            {
                return;
            }

            if (!settings.ShowNotifications || !settings.NotifyNativeHdrConflict)
            {
                settings.NativeHdrConflictNotified = true;
                SavePluginSettings(settings);
                return;
            }

            settings.NativeHdrConflictNotified = true;
            SavePluginSettings(settings);
            try
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "display-manager-native-hdr-cleared",
                    Loc("LOCDisplayManager_NativeHdrConflictNotice"),
                    NotificationType.Info));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to show native HDR conflict notification.");
            }
        }

        public string ArmRestoreLeaseForTest()
        {
            var snapshot = Topology.CaptureSnapshot();
            snapshot.HdrRestoreWrites = hdr.BuildForceOffWrites(Displays.GetDisplays());
            EnsureRestoreClient();
            var path = restoreClient.Arm(snapshot);
            StartRestoreHeartbeat();
            return path;
        }

        public void DisarmRestoreLease()
        {
            StopRestoreHeartbeat();
            restoreClient?.Disarm();
        }

        public DisplayTopologyApplyResult ApplyTopologyWithLease(DisplayTopologyRequest request)
        {
            EnsureRestoreClient();
            DisplaySnapshot before = null;
            try
            {
                before = Topology.CaptureSnapshot();
                before.HdrRestoreWrites = hdr.BuildForceOffWrites(Displays.GetDisplays());
                restoreClient.Arm(before);
                StartRestoreHeartbeat();
            }
            catch (Exception ex)
            {
                return new DisplayTopologyApplyResult
                {
                    Success = false,
                    Error = "Could not arm restore lease: " + ex.Message
                };
            }

            var apply = Topology.TryApplyRequest(request);
            if (!apply.Success)
            {
                if (before != null)
                {
                    Topology.TryRestoreSnapshot(before, out _);
                }

                DisarmRestoreLease();
                return apply;
            }

            apply.BeforeSnapshot = before ?? apply.BeforeSnapshot;
            return apply;
        }

        public bool TryRestoreLastLeaseSnapshot(DisplaySnapshot snapshot, out string error)
        {
            var ok = Topology.TryRestoreSnapshot(snapshot, out error);
            DisarmRestoreLease();
            NotifyDisplaysChanged();
            return ok;
        }

        public bool TryWriteHdrOffNow(out string error)
        {
            var writes = hdr.BuildForceOffWrites(Displays.GetDisplays());
            if (writes.Count == 0)
            {
                error = "No advanced-color-capable active display was found.";
                return false;
            }

            var count = hdr.ApplyHdrWrites(writes, out error);
            NotifyDisplaysChanged();
            return count > 0;
        }

        public string GetHdrPolicyOverviewText()
        {
            switch (settings?.GlobalHdrPolicy ?? GlobalHdrPolicy.DoNotManage)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    return Loc("LOCDisplayManager_HdrPolicyAllGames");
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    return Loc("LOCDisplayManager_HdrPolicyMetadata");
                case GlobalHdrPolicy.UsePlayniteNative:
                    return Loc("LOCDisplayManager_HdrPolicyPlayniteNative");
                default:
                    return Loc("LOCDisplayManager_HdrPolicyNone");
            }
        }

        public string GetSelectedGameHdrOverviewText()
        {
            try
            {
                var selected = PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();
                if (selected == null)
                {
                    return Loc("LOCDisplayManager_OverviewGameHdrNone");
                }

                var plan = PlanHdrSession(selected);
                return string.Format(
                    Loc("LOCDisplayManager_OverviewGameHdrSimpleFormat"),
                    selected.Name,
                    DescribePlanAction(plan));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to build selected-game HDR overview.");
                return Loc("LOCDisplayManager_OverviewGameHdrNone");
            }
        }

        public int GetGameHdrOverrideCount()
        {
            return gameProfiles?.CountNonInherit() ?? 0;
        }

        public List<GameDisplayProfileEntry> GetGameProfileEntries()
        {
            var snapshots = gameProfiles?.GetProfilesSnapshot() ?? new Dictionary<Guid, GameDisplayProfile>();
            var games = PlayniteApi.Database.Games
                .GroupBy(game => game.Id)
                .ToDictionary(group => group.Key, group => group.First());

            return snapshots
                .Where(item => item.Value != null && !item.Value.IsEmpty)
                .Select(item =>
                {
                    games.TryGetValue(item.Key, out var game);
                    return new GameDisplayProfileEntry
                    {
                        GameId = item.Key,
                        GameName = !string.IsNullOrWhiteSpace(game?.Name)
                            ? game.Name
                            : Loc("LOCDisplayManager_UnknownGame") + " (" + item.Key + ")",
                        GameImagePath = GetGameProfileImagePath(game),
                        HdrOverride = item.Value.HdrOverride,
                        RefreshRateOverride = item.Value.RefreshRateOverride,
                        PreferredRefreshRateHz = item.Value.PreferredRefreshRateHz,
                        ResolutionOverride = item.Value.ResolutionOverride,
                        PreferredResolutionWidth = item.Value.PreferredResolutionWidth,
                        PreferredResolutionHeight = item.Value.PreferredResolutionHeight,
                        PreferredPlayDisplayId = item.Value.PreferredPlayDisplayId,
                        DisplayProfileId = item.Value.DisplayProfileId,
                        TurnOffOtherDisplaysOverride = item.Value.TurnOffOtherDisplaysOverride
                    };
                })
                .OrderBy(e => e.GameName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void ReplaceGameProfiles(IEnumerable<GameDisplayProfileEntry> entries)
        {
            var map = (entries ?? Enumerable.Empty<GameDisplayProfileEntry>())
                .Where(e => e != null && e.GameId != Guid.Empty)
                .Select(e => new KeyValuePair<Guid, GameDisplayProfile>(e.GameId, e.ToProfile()));
            gameProfiles?.ReplaceProfiles(map);
        }

        public List<PlatformProfileEntry> GetPlatformProfileEntries()
        {
            var snapshots = platformProfiles?.GetProfilesSnapshot()
                ?? new Dictionary<Guid, GameDisplayProfile>();
            var platforms = PlayniteApi.Database.Platforms
                .GroupBy(p => p.Id)
                .ToDictionary(g => g.Key, g => g.First());

            return snapshots
                .Where(item => item.Value != null && !item.Value.IsEmpty)
                .Select(item =>
                {
                    platforms.TryGetValue(item.Key, out var platform);
                    return new PlatformProfileEntry
                    {
                        PlatformId = item.Key,
                        PlatformName = !string.IsNullOrWhiteSpace(platform?.Name)
                            ? platform.Name
                            : Loc("LOCDisplayManager_UnknownPlatform") + " (" + item.Key + ")",
                        HdrOverride = item.Value.HdrOverride,
                        RefreshRateOverride = item.Value.RefreshRateOverride,
                        PreferredRefreshRateHz = item.Value.PreferredRefreshRateHz,
                        ResolutionOverride = item.Value.ResolutionOverride,
                        PreferredResolutionWidth = item.Value.PreferredResolutionWidth,
                        PreferredResolutionHeight = item.Value.PreferredResolutionHeight,
                        PreferredPlayDisplayId = item.Value.PreferredPlayDisplayId,
                        DisplayProfileId = item.Value.DisplayProfileId,
                        TurnOffOtherDisplaysOverride = item.Value.TurnOffOtherDisplaysOverride
                    };
                })
                .OrderBy(e => e.PlatformName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void ReplacePlatformProfiles(IEnumerable<PlatformProfileEntry> entries)
        {
            var map = (entries ?? Enumerable.Empty<PlatformProfileEntry>())
                .Where(e => e != null && e.PlatformId != Guid.Empty)
                .Select(e => new KeyValuePair<Guid, GameDisplayProfile>(e.PlatformId, e.ToProfile()));
            platformProfiles?.ReplaceProfiles(map);
        }

        public bool ConfirmRemovePlatformProfile(string platformName)
        {
            var message = string.Format(
                Loc("LOCDisplayManager_ConfirmRemovePlatformProfileMessage"),
                platformName ?? Loc("LOCDisplayManager_UnknownPlatform"));
            return PlayniteApi.Dialogs.ShowMessage(
                message,
                Loc("LOCDisplayManager_ConfirmRemoveProfileTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        public bool ConfirmRemoveGameProfile(string gameName)
        {
            var message = string.Format(
                Loc("LOCDisplayManager_ConfirmRemoveProfilePendingMessage"),
                gameName ?? Loc("LOCDisplayManager_UnknownGame"));
            return PlayniteApi.Dialogs.ShowMessage(
                message,
                Loc("LOCDisplayManager_ConfirmRemoveProfileTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private string GetGameProfileImagePath(Game game)
        {
            if (game == null)
            {
                return null;
            }

            try
            {
                var cover = ResolveGameMediaPath(game.CoverImage);
                if (!string.IsNullOrWhiteSpace(cover))
                {
                    return cover;
                }

                return ResolveGameMediaPath(game.Icon);
            }
            catch
            {
                // Cover/icon paths are best-effort for the profiles list.
            }

            return null;
        }

        private string ResolveGameMediaPath(string mediaPath)
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                return null;
            }

            try
            {
                var full = PlayniteApi?.Database?.GetFullFilePath(mediaPath);
                if (!string.IsNullOrWhiteSpace(full) && File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                // Fall through to the raw path.
            }

            return File.Exists(mediaPath) ? mediaPath : null;
        }

        private string DescribePlanAction(HdrSessionPlan plan)
        {
            if (plan == null)
            {
                return Loc("LOCDisplayManager_GameHdrInherit");
            }

            switch (plan.EffectiveOverride)
            {
                case GameHdrOverride.ForceOn:
                    return Loc("LOCDisplayManager_GameHdrForceOn");
                case GameHdrOverride.ForceOff:
                    return Loc("LOCDisplayManager_GameHdrForceOff");
                case GameHdrOverride.DoNotTouch:
                    return Loc("LOCDisplayManager_GameHdrDoNotTouch");
            }

            switch (plan.Action)
            {
                case HdrSessionAction.Enable:
                    return Loc("LOCDisplayManager_SessionActionEnable");
                case HdrSessionAction.Disable:
                    return Loc("LOCDisplayManager_SessionActionDisable");
                default:
                    return Loc("LOCDisplayManager_SessionActionNone");
            }
        }

        private GameMenuItem CreateHdrOverrideMenuItem(
            string menuSection,
            string description,
            GetGameMenuItemsArgs request,
            GameHdrOverride hdrOverride)
        {
            return new GameMenuItem
            {
                MenuSection = menuSection,
                Description = description,
                Action = actionArgs =>
                {
                    var games = actionArgs?.Games ?? request.Games;
                    if (games == null)
                    {
                        return;
                    }

                    foreach (var game in games)
                    {
                        gameProfiles.SetHdrOverride(game, hdrOverride);
                    }
                }
            };
        }

        private GameMenuItem CreateRefreshOverrideMenuItem(
            string menuSection,
            string description,
            GetGameMenuItemsArgs request,
            GameRefreshRateOverride refreshOverride,
            double? preferredHz = null)
        {
            return new GameMenuItem
            {
                MenuSection = menuSection,
                Description = description,
                Action = actionArgs =>
                {
                    var games = actionArgs?.Games ?? request.Games;
                    if (games == null)
                    {
                        return;
                    }

                    foreach (var game in games)
                    {
                        gameProfiles.SetRefreshRateOverride(game, refreshOverride, preferredHz);
                    }
                }
            };
        }

        private GameMenuItem CreateResolutionOverrideMenuItem(
            string menuSection,
            string description,
            GetGameMenuItemsArgs request,
            GameResolutionOverride resolutionOverride,
            int? preferredWidth = null,
            int? preferredHeight = null)
        {
            return new GameMenuItem
            {
                MenuSection = menuSection,
                Description = description,
                Action = actionArgs =>
                {
                    var games = actionArgs?.Games ?? request.Games;
                    if (games == null)
                    {
                        return;
                    }

                    foreach (var game in games)
                    {
                        gameProfiles.SetResolutionOverride(game, resolutionOverride, preferredWidth, preferredHeight);
                    }
                }
            };
        }

        private GameMenuItem CreatePlayDisplayMenuItem(
            string menuSection,
            string description,
            GetGameMenuItemsArgs request,
            string displayId)
        {
            return new GameMenuItem
            {
                MenuSection = menuSection,
                Description = description,
                Action = actionArgs =>
                {
                    var games = actionArgs?.Games ?? request.Games;
                    if (games == null)
                    {
                        return;
                    }

                    var aggregate = new DisplayOverrideSanitizeResult();
                    foreach (var game in games)
                    {
                        gameProfiles.SetPreferredPlayDisplayId(game, displayId);
                        aggregate.Merge(SanitizeGameProfileForCurrentPlayDisplay(game));
                    }

                    if (aggregate.Changed)
                    {
                        ShowOverrideResetMessage(aggregate);
                    }
                }
            };
        }

        private GameMenuItem CreateTurnOffOthersMenuItem(
            string menuSection,
            string description,
            GetGameMenuItemsArgs request,
            bool? turnOffOtherDisplays)
        {
            return new GameMenuItem
            {
                MenuSection = menuSection,
                Description = description,
                Action = actionArgs =>
                {
                    var games = actionArgs?.Games ?? request.Games;
                    if (games == null)
                    {
                        return;
                    }

                    foreach (var game in games)
                    {
                        gameProfiles.SetTurnOffOtherDisplaysOverride(game, turnOffOtherDisplays);
                    }
                }
            };
        }

        /// <returns>Details of what was reset; silent when called from menu open heal.</returns>
        private DisplayOverrideSanitizeResult SanitizeGameProfileForCurrentPlayDisplay(Game game)
        {
            var result = new DisplayOverrideSanitizeResult();
            if (game == null || gameProfiles == null)
            {
                return result;
            }

            var target = ResolveMenuTargetDisplay(new[] { game });
            if (target == null)
            {
                return result;
            }

            gameProfiles.UpdateProfile(game, profile =>
            {
                result = DisplayOverrideSanitizer.SanitizeGameProfile(
                    profile,
                    target,
                    Resolutions,
                    RefreshRates,
                    IsDisplayHdrSupported);
            });

            return result;
        }

        public void ShowOverrideResetMessage(
            DisplayOverrideSanitizeResult result,
            bool useNativeDefault = false)
        {
            if (result == null || !result.Changed)
            {
                return;
            }

            var after = useNativeDefault
                ? Loc("LOCDisplayManager_OverrideResetAfterNative")
                : Loc("LOCDisplayManager_OverrideResetAfterInherit");
            var changes = new List<MessageDialogChange>();

            if (result.ResetResolution)
            {
                var format = Loc("LOCDisplayManager_OverrideResetResolutionFormat");
                changes.Add(new MessageDialogChange
                {
                    Before = string.Format(
                        format,
                        result.ResetResolutionLabel ?? Loc("LOCDisplayManager_GameMenuResolutionSection")),
                    After = after
                });
            }

            if (result.ResetRefreshRate)
            {
                var format = Loc("LOCDisplayManager_OverrideResetRefreshFormat");
                var hz = result.ResetRefreshRateHz.HasValue
                    ? result.ResetRefreshRateHz.Value.ToString("0.###")
                    : "?";
                changes.Add(new MessageDialogChange
                {
                    Before = string.Format(format, hz),
                    After = after
                });
            }

            if (result.ResetHdr)
            {
                changes.Add(new MessageDialogChange
                {
                    Before = Loc("LOCDisplayManager_OverrideResetHdr"),
                    After = after
                });
            }

            ShowNarianMessage(
                Loc("LOCDisplayManager_OverrideResetTitle"),
                Loc("LOCDisplayManager_OverrideResetIntro"),
                changes);
        }

        public void ShowNarianMessage(
            string title,
            string message,
            IEnumerable<MessageDialogChange> changes = null)
        {
            try
            {
                var window = new MessageDialogWindow(
                    title,
                    message,
                    Loc("LOCDisplayManager_DialogOk"),
                    changes);
                var owner = PlayniteApi?.Dialogs?.GetCurrentAppWindow();
                if (owner != null)
                {
                    window.Owner = owner;
                }
                else if (Application.Current?.MainWindow != null)
                {
                    window.Owner = Application.Current.MainWindow;
                }

                SettingsAppearance.ApplyWindow(window, settings?.AppearancePreset);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to show Display Manager message dialog.");
                PlayniteApi?.Dialogs?.ShowMessage(message ?? string.Empty, title ?? "Display Manager");
            }
        }

        private bool IsDisplayHdrSupported(DisplayInfo display)
        {
            if (display == null || Hdr == null)
            {
                return false;
            }

            try
            {
                var probe = Hdr.ProbeActiveTargets(new[] { display }).FirstOrDefault();
                return probe != null && probe.HdrSupported;
            }
            catch
            {
                return false;
            }
        }

        private string DescribePlayDisplayOverride(string displayId)
        {
            if (displayId == null)
            {
                return Loc("LOCDisplayManager_GameDisplayInherit");
            }

            if (string.IsNullOrEmpty(displayId))
            {
                return Loc("LOCDisplayManager_PlayDisplayWindowsDefault");
            }

            var match = Displays.GetDisplays()
                .FirstOrDefault(d => string.Equals(d.Id, displayId, StringComparison.OrdinalIgnoreCase));
            return match?.EffectiveName ?? displayId;
        }

        private string DescribeHdrOverride(GameHdrOverride hdrOverride)
        {
            switch (hdrOverride)
            {
                case GameHdrOverride.ForceOn:
                    return Loc("LOCDisplayManager_GameHdrForceOn");
                case GameHdrOverride.ForceOff:
                    return Loc("LOCDisplayManager_GameHdrForceOff");
                case GameHdrOverride.DoNotTouch:
                    return Loc("LOCDisplayManager_GameHdrDoNotTouch");
                default:
                    return Loc("LOCDisplayManager_GameHdrInherit");
            }
        }

        private string DescribeRefreshOverride(GameRefreshRateOverride refreshOverride, double? preferredHz = null)
        {
            switch (refreshOverride)
            {
                case GameRefreshRateOverride.Native:
                    return Loc("LOCDisplayManager_GameHzNative");
                case GameRefreshRateOverride.ExactHz:
                    if (preferredHz.HasValue)
                    {
                        return string.Format(Loc("LOCDisplayManager_RefreshPolicyExactFormat"), preferredHz.Value);
                    }

                    return Loc("LOCDisplayManager_RefreshPolicyExact");
                case GameRefreshRateOverride.Prefer60:
                    return Loc("LOCDisplayManager_GameHz60");
                case GameRefreshRateOverride.Prefer120:
                    return Loc("LOCDisplayManager_GameHz120");
                case GameRefreshRateOverride.HighestDetected:
                    return Loc("LOCDisplayManager_GameHzHighest");
                default:
                    return Loc("LOCDisplayManager_GameHzInherit");
            }
        }

        private string DescribeResolutionOverride(
            GameResolutionOverride resolutionOverride,
            int? preferredWidth = null,
            int? preferredHeight = null)
        {
            switch (resolutionOverride)
            {
                case GameResolutionOverride.Native:
                    return Loc("LOCDisplayManager_GameResolutionNative");
                case GameResolutionOverride.Exact:
                    if (preferredWidth > 0 && preferredHeight > 0)
                    {
                        return string.Format(
                            Loc("LOCDisplayManager_ResolutionPolicyExactFormat"),
                            preferredWidth.Value,
                            preferredHeight.Value);
                    }

                    return Loc("LOCDisplayManager_ResolutionPolicyExact");
                case GameResolutionOverride.LowestAvailable:
                    return Loc("LOCDisplayManager_GameResolutionLowest");
                case GameResolutionOverride.HighestAvailable:
                    return Loc("LOCDisplayManager_GameResolutionHighest");
                default:
                    return Loc("LOCDisplayManager_GameResolutionInherit");
            }
        }

        private static string CheckedMenuLabel(bool isChecked, string description)
        {
            return isChecked ? "✓ " + description : description;
        }

        private void EnsureRestoreClient()
        {
            if (restoreClient == null)
            {
                restoreClient = new DisplayRestoreClient(logger);
            }
        }

        private void StartRestoreHeartbeat()
        {
            if (restoreHeartbeatTimer != null)
            {
                return;
            }

            restoreHeartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            restoreHeartbeatTimer.Tick += (_, __) => restoreClient?.Heartbeat();
            restoreHeartbeatTimer.Start();
        }

        private void StopRestoreHeartbeat()
        {
            if (restoreHeartbeatTimer == null)
            {
                return;
            }

            restoreHeartbeatTimer.Stop();
            restoreHeartbeatTimer = null;
        }

        private void EnsureEnglishFallbackResources()
        {
            try
            {
                englishFallbackResources = LoadEnglishFallbackResources();
                if (englishFallbackResources == null || Application.Current?.Resources == null)
                {
                    return;
                }

                var alreadyLoaded = Application.Current.Resources.MergedDictionaries
                    .OfType<ResourceDictionary>()
                    .Any(a => ReferenceEquals(a, englishFallbackResources) ||
                              (a.Contains("LOCDisplayManager_PluginName") &&
                               Equals(a["LOCDisplayManager_PluginName"], "Display Manager")));
                if (!alreadyLoaded)
                {
                    Application.Current.Resources.MergedDictionaries.Insert(0, englishFallbackResources);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load English fallback localization.");
            }
        }

        private ResourceDictionary LoadEnglishFallbackResources()
        {
            var path = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location) ?? string.Empty,
                "Localization", "en_US.xaml");
            if (!File.Exists(path))
            {
                return null;
            }

            using (var stream = File.OpenRead(path))
            {
                return XamlReader.Load(stream) as ResourceDictionary;
            }
        }

        private string GetEnglishFallbackString(string key)
        {
            if (englishFallbackResources == null || !englishFallbackResources.Contains(key))
            {
                return null;
            }

            return englishFallbackResources[key] as string;
        }
    }
}
