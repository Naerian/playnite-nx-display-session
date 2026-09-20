using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using PlayniteDisplayManager.Profiles;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Restore;
using PlayniteDisplayManager.Theme;

namespace PlayniteDisplayManager
{
    public sealed class PlayniteDisplayManagerPlugin : GenericPlugin
    {
        private readonly ILogger logger;
        private readonly HdrService hdr = new HdrService();
        private readonly RefreshRateService refreshRates = new RefreshRateService();
        private DisplayManagerSettings settings;
        private GameDisplayProfileStore gameProfiles;
        private NativeHdrFlagMigration nativeHdrMigration;
        private ResourceDictionary englishFallbackResources;
        private DisplayRestoreClient restoreClient;
        private DispatcherTimer restoreHeartbeatTimer;
        private DisplaySnapshot gameSessionSnapshot;
        private Guid? activeGameId;
        private string activeGameName;
        private TopPanelItem desktopTopPanelItem;
        private bool openingStandaloneSettings;

        public override Guid Id { get; } = Guid.Parse("9c2e4a71-b8d3-4f6a-a1c5-0e7d92f3b846");

        public DisplayEnumerator Displays { get; }

        public DisplayTopologyService Topology { get; }

        public HdrService Hdr => hdr;

        public RefreshRateService RefreshRates => refreshRates;

        public GameDisplayProfileStore GameProfiles => gameProfiles;

        public DisplayManagerThemeApi Theme { get; }

        public PlayniteDisplayManagerPlugin(IPlayniteAPI playniteApi) : base(playniteApi)
        {
            logger = LogManager.GetLogger();
            Displays = new DisplayEnumerator();
            Topology = new DisplayTopologyService();
            gameProfiles = new GameDisplayProfileStore(GetPluginUserDataPath());
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
                    "HdrStatus"
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
        }

        public DisplayManagerSettings Settings => settings;

        internal event EventHandler DisplaysChanged;

        public void NotifyDisplaysChanged()
        {
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
            TryOfferFirstRunSetupWizard();
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

            var draft = new SetupWizardDraft
            {
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
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var games = args?.Games?.ToList();
            if (games == null || games.Count == 0)
            {
                yield break;
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

            var primary = Displays.GetDisplays()
                .FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? Displays.GetDisplays().FirstOrDefault(d => d.IsConnected);
            var availableRates = RefreshRates?.GetAvailableRates(primary) ?? Array.Empty<double>();
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

            foreach (var display in Displays.GetDisplays().Where(d => d.IsConnected))
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
        }

        /// <summary>
        /// Resolves the play display for a game: per-game override wins, then global setting.
        /// null/empty means keep the current Windows primary.
        /// </summary>
        private string ResolvePreferredPlayDisplayId(Game game)
        {
            var profile = gameProfiles?.GetProfile(game);
            if (profile != null && profile.HasPlayDisplayOverride)
            {
                return profile.PreferredPlayDisplayId;
            }

            return settings?.PreferredPlayDisplayId;
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

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            EndGameSession("app-stopped");
            StopRestoreHeartbeat();
            if (restoreClient != null)
            {
                try { restoreClient.Disarm(); } catch { /* ignore */ }
                restoreClient.Dispose();
                restoreClient = null;
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

            var hdrPlan = PlanHdrSession(game);
            var hzPlan = PlanRefreshRateSession(game);

            var livePreview = Displays.GetDisplays().ToList();
            var currentPrimary = livePreview.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? livePreview.FirstOrDefault(d => d.IsConnected);
            var preferredId = ResolvePreferredPlayDisplayId(game);
            var preferredExists = !string.IsNullOrWhiteSpace(preferredId)
                && livePreview.Any(d => d.IsConnected
                    && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            var wantsMakePrimary = preferredExists
                && (currentPrimary == null
                    || !string.Equals(currentPrimary.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            var topologyTargetId = preferredExists
                ? preferredId
                : currentPrimary?.Id;
            var wantsTurnOffOthers = settings.TurnOffOtherDisplaysOnLaunch
                && !string.IsNullOrWhiteSpace(topologyTargetId);
            var wantsTopology = wantsMakePrimary || wantsTurnOffOthers;

            if (hdrPlan.Action == HdrSessionAction.None && !hzPlan.ShouldApply && !wantsTopology)
            {
                logger.Info("Display session skipped for " + game.Name +
                            " (HDR: " + hdrPlan.Reason + "; Hz: " + hzPlan.Reason + "; topology: none).");
                return;
            }

            try
            {
                EndGameSession("replace-session");

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

                var snapshot = Topology.CaptureSnapshot();
                snapshot.HdrRestoreWrites = offWrites;
                gameSessionSnapshot = snapshot;
                activeGameId = game.Id;
                activeGameName = game.Name;

                EnsureRestoreClient();
                restoreClient.Arm(snapshot);
                StartRestoreHeartbeat();

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
                        logger.Warn("Topology apply failed for " + activeGameName + ": " + topologyApply.Error);
                    }
                    else
                    {
                        logger.Info("Topology applied for " + activeGameName + " (" + topologyApply.Message + ").");
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

                if (hzPlan.ShouldApply && hzPlan.TargetHz.HasValue && primary != null)
                {
                    if (refreshRates.TryApply(primary, hzPlan.TargetHz.Value, out var hzError))
                    {
                        logger.Info("Refresh rate set to " + hzPlan.TargetHz.Value.ToString("0.###") +
                                    " Hz for " + activeGameName + " (" + hzPlan.Reason + ").");
                    }
                    else
                    {
                        logger.Warn("Refresh rate apply failed (" + hzPlan.Reason + "): " + hzError);
                    }
                }

                if (hdrPlan.Action == HdrSessionAction.None)
                {
                    return;
                }

                if (applyWrites.Count == 0)
                {
                    logger.Info("HDR plan " + hdrPlan.Action + " (" + hdrPlan.Reason +
                                "): no advanced-color-capable target; lease armed for topology/Hz.");
                    return;
                }

                var written = hdr.ApplyHdrWrites(applyWrites, out var error);
                if (written == 0)
                {
                    logger.Warn("HDR write failed (" + hdrPlan.Reason + "): " + error);
                }
                else
                {
                    logger.Info("HDR " + hdrPlan.Action + " wrote " + written +
                                " target(s) for " + activeGameName + " (" + hdrPlan.Reason + ").");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to begin Display Manager game session.");
            }
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

                if (snapshot != null)
                {
                    // Restore writes HDR off from snapshot.HdrRestoreWrites — no GET trust.
                    if (!Topology.TryRestoreSnapshot(snapshot, out var error))
                    {
                        logger.Warn("Session restore failed (" + reason + "): " + error);
                        if (snapshot.HdrRestoreWrites != null && snapshot.HdrRestoreWrites.Count > 0)
                        {
                            hdr.ApplyHdrWrites(snapshot.HdrRestoreWrites, out _);
                        }
                    }
                    else
                    {
                        logger.Info("Session restored (" + reason + ")" +
                                    (string.IsNullOrWhiteSpace(name) ? "." : " for " + name + "."));
                    }
                }

                DisarmRestoreLease();
                NotifyDisplaysChanged();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to end Display Manager game session (" + reason + ").");
            }
        }

        public HdrSessionPlan PlanHdrSession(Game game)
        {
            return HdrSessionPlanner.Plan(
                game,
                settings?.GlobalHdrPolicy ?? GlobalHdrPolicy.DoNotManage,
                gameProfiles?.GetProfile(game),
                settings?.HdrMetadataMatchNames,
                settings?.IncludeTagsInHdrMetadataMatch ?? false);
        }

        public RefreshRatePlan PlanRefreshRateSession(Game game)
        {
            var primary = Displays.GetDisplays()
                .FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? Displays.GetDisplays().FirstOrDefault(d => d.IsConnected);
            var profile = gameProfiles?.GetProfile(game);
            var gameOverride = profile?.RefreshRateOverride ?? GameRefreshRateOverride.Inherit;
            double? preferredHz = settings?.PreferredRefreshRateHz;
            if (gameOverride == GameRefreshRateOverride.ExactHz
                || gameOverride == GameRefreshRateOverride.Prefer60
                || gameOverride == GameRefreshRateOverride.Prefer120)
            {
                preferredHz = profile?.PreferredRefreshRateHz ?? preferredHz;
            }

            return refreshRates.Plan(
                settings?.GlobalRefreshRatePolicy ?? RefreshRatePolicy.Native,
                gameOverride,
                primary,
                preferredHz);
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

        public string GetNightLightOverviewText()
        {
            return Loc("LOCDisplayManager_OverviewNightLightCut");
        }

        public string GetRefreshRateOverviewText()
        {
            switch (settings?.GlobalRefreshRatePolicy ?? RefreshRatePolicy.Native)
            {
                case RefreshRatePolicy.ExactHz:
                case RefreshRatePolicy.Prefer60:
                case RefreshRatePolicy.Prefer120:
                    if (settings?.PreferredRefreshRateHz > 0)
                    {
                        return string.Format(
                            Loc("LOCDisplayManager_RefreshPolicyExactFormat"),
                            settings.PreferredRefreshRateHz.Value);
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
            switch (settings?.GlobalHdrPolicy ?? GlobalHdrPolicy.DoNotManage)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    return Loc("LOCDisplayManager_OverviewActionAlwaysOn");
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    return Loc("LOCDisplayManager_OverviewActionMetadata");
                default:
                    return Loc("LOCDisplayManager_OverviewActionDoNotManage");
            }
        }

        public string GetActiveSessionOverviewText()
        {
            if (activeGameId.HasValue && !string.IsNullOrWhiteSpace(activeGameName))
            {
                return string.Format(Loc("LOCDisplayManager_OverviewSessionActiveFormat"), activeGameName);
            }

            return Loc("LOCDisplayManager_OverviewSessionIdle");
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
                        PreferredPlayDisplayId = item.Value.PreferredPlayDisplayId
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
                if (!string.IsNullOrWhiteSpace(game.Icon) && File.Exists(game.Icon))
                {
                    return game.Icon;
                }

                if (!string.IsNullOrWhiteSpace(game.CoverImage) && File.Exists(game.CoverImage))
                {
                    return game.CoverImage;
                }
            }
            catch
            {
                // Cover/icon paths are best-effort for the profiles list.
            }

            return null;
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

                    var first = games.FirstOrDefault();
                    if (first != null)
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            first.Name + ": " + DescribeHdrOverride(hdrOverride),
                            Loc("LOCDisplayManager_PluginName"));
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

                    var first = games.FirstOrDefault();
                    if (first != null)
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            first.Name + ": " + DescribeRefreshOverride(refreshOverride, preferredHz),
                            Loc("LOCDisplayManager_PluginName"));
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

                    foreach (var game in games)
                    {
                        gameProfiles.SetPreferredPlayDisplayId(game, displayId);
                    }

                    var first = games.FirstOrDefault();
                    if (first != null)
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            first.Name + ": " + DescribePlayDisplayOverride(displayId),
                            Loc("LOCDisplayManager_PluginName"));
                    }
                }
            };
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
