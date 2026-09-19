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
using PlayniteDisplayManager.Audio;
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
        private AudioSwitcherBridge audioSwitcher;
        private DisplayManagerSettings settings;
        private GameDisplayProfileStore gameProfiles;
        private NativeHdrFlagMigration nativeHdrMigration;
        private ResourceDictionary englishFallbackResources;
        private DisplayRestoreClient restoreClient;
        private DispatcherTimer restoreHeartbeatTimer;
        private DisplaySnapshot gameSessionSnapshot;
        private Guid? activeGameId;
        private string activeGameName;
        private string sessionPreviousAudioDeviceId;
        private bool sessionAppliedAudioDevice;
        private TopPanelItem desktopTopPanelItem;
        private bool openingStandaloneSettings;

        public override Guid Id { get; } = Guid.Parse("9c2e4a71-b8d3-4f6a-a1c5-0e7d92f3b846");

        public DisplayEnumerator Displays { get; }

        public DisplayTopologyService Topology { get; }

        public HdrService Hdr => hdr;

        public RefreshRateService RefreshRates => refreshRates;

        public AudioSwitcherBridge AudioSwitcher => audioSwitcher;

        public GameDisplayProfileStore GameProfiles => gameProfiles;

        public DisplayManagerThemeApi Theme { get; }

        public PlayniteDisplayManagerPlugin(IPlayniteAPI playniteApi) : base(playniteApi)
        {
            logger = LogManager.GetLogger();
            Displays = new DisplayEnumerator();
            Topology = new DisplayTopologyService();
            gameProfiles = new GameDisplayProfileStore(GetPluginUserDataPath());
            nativeHdrMigration = new NativeHdrFlagMigration(PlayniteApi, logger, GetPluginUserDataPath());
            audioSwitcher = new AudioSwitcherBridge(PlayniteApi, logger);
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

            yield return CreateHdrOverrideMenuItem(
                hdrSection,
                CheckedMenuLabel(hdrCurrent.All(o => o == GameHdrOverride.DoNotTouch),
                    Loc("LOCDisplayManager_GameHdrDoNotTouch")),
                args,
                GameHdrOverride.DoNotTouch);

            var hzSection = "Display Manager|" + Loc("LOCDisplayManager_GameMenuHzSection");
            var hzCurrent = games.Select(g => gameProfiles.GetRefreshRateOverride(g)).ToList();

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

            yield return CreateRefreshOverrideMenuItem(
                hzSection,
                CheckedMenuLabel(hzCurrent.All(o => o == GameRefreshRateOverride.Prefer60),
                    Loc("LOCDisplayManager_GameHz60")),
                args,
                GameRefreshRateOverride.Prefer60);

            yield return CreateRefreshOverrideMenuItem(
                hzSection,
                CheckedMenuLabel(hzCurrent.All(o => o == GameRefreshRateOverride.Prefer120),
                    Loc("LOCDisplayManager_GameHz120")),
                args,
                GameRefreshRateOverride.Prefer120);

            yield return CreateRefreshOverrideMenuItem(
                hzSection,
                CheckedMenuLabel(hzCurrent.All(o => o == GameRefreshRateOverride.HighestDetected),
                    Loc("LOCDisplayManager_GameHzHighest")),
                args,
                GameRefreshRateOverride.HighestDetected);

            var audioSection = "Display Manager|" + Loc("LOCDisplayManager_GameMenuAudioSection");
            var associated = games.Count == 1
                ? gameProfiles.GetAssociatedAudioDeviceId(games[0])
                : null;

            yield return CreateAudioDeviceMenuItem(
                audioSection,
                CheckedMenuLabel(string.IsNullOrWhiteSpace(associated),
                    Loc("LOCDisplayManager_GameAudioNone")),
                args,
                null);

            if (audioSwitcher != null && audioSwitcher.IsAvailable)
            {
                foreach (var device in audioSwitcher.GetPlaybackDevices())
                {
                    var deviceId = device.Id;
                    yield return CreateAudioDeviceMenuItem(
                        audioSection,
                        CheckedMenuLabel(
                            string.Equals(associated, deviceId, StringComparison.OrdinalIgnoreCase),
                            device.Name),
                        args,
                        deviceId);
                }
            }
            else
            {
                yield return new GameMenuItem
                {
                    MenuSection = audioSection,
                    Description = Loc("LOCDisplayManager_AudioSwitcherMissing"),
                    Action = _ => { }
                };
            }
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
            var audioDeviceId = settings.EnableAudioSwitcherHook
                ? gameProfiles?.GetAssociatedAudioDeviceId(game)
                : null;
            var wantsAudio = !string.IsNullOrWhiteSpace(audioDeviceId) &&
                             audioSwitcher != null &&
                             audioSwitcher.IsAvailable;

            if (hdrPlan.Action == HdrSessionAction.None && !hzPlan.ShouldApply && !wantsAudio)
            {
                logger.Info("Display session skipped for " + game.Name +
                            " (HDR: " + hdrPlan.Reason + "; Hz: " + hzPlan.Reason + "; audio: none).");
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
                sessionPreviousAudioDeviceId = null;
                sessionAppliedAudioDevice = false;

                EnsureRestoreClient();
                restoreClient.Arm(snapshot);
                StartRestoreHeartbeat();

                if (wantsAudio)
                {
                    sessionPreviousAudioDeviceId = audioSwitcher.TryGetCurrentPlaybackDeviceId();
                    if (audioSwitcher.TrySetPlaybackDevice(audioDeviceId, out var audioError))
                    {
                        sessionAppliedAudioDevice = true;
                        logger.Info("Audio Switcher playback set for " + activeGameName + ".");
                    }
                    else
                    {
                        logger.Warn("Audio Switcher hook failed: " + audioError);
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
                                "): no advanced-color-capable target; lease armed for topology/Hz/audio.");
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
                var restoreAudio = sessionAppliedAudioDevice;
                var previousAudio = sessionPreviousAudioDeviceId;
                sessionAppliedAudioDevice = false;
                sessionPreviousAudioDeviceId = null;

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

                if (restoreAudio && !string.IsNullOrWhiteSpace(previousAudio) && audioSwitcher != null)
                {
                    if (audioSwitcher.TrySetPlaybackDevice(previousAudio, out var audioError))
                    {
                        logger.Info("Audio Switcher playback restored after session.");
                    }
                    else
                    {
                        logger.Warn("Audio Switcher restore failed: " + audioError);
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
            return refreshRates.Plan(
                settings?.GlobalRefreshRatePolicy ?? RefreshRatePolicy.Native,
                gameProfiles?.GetRefreshRateOverride(game) ?? GameRefreshRateOverride.Inherit,
                primary);
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
                case RefreshRatePolicy.Prefer60:
                    return Loc("LOCDisplayManager_RefreshPolicy60");
                case RefreshRatePolicy.Prefer120:
                    return Loc("LOCDisplayManager_RefreshPolicy120");
                case RefreshRatePolicy.HighestDetected:
                    return Loc("LOCDisplayManager_RefreshPolicyHighest");
                default:
                    return Loc("LOCDisplayManager_RefreshPolicyNative");
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

            if (!nativeHdrMigration.TryClearGame(game))
            {
                return;
            }

            if (settings == null || settings.NativeHdrConflictNotified)
            {
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

                var hasMeta = GameHasHdrMetadata(selected);
                var plan = PlanHdrSession(selected);
                var metaText = hasMeta
                    ? Loc("LOCDisplayManager_OverviewGameHdrYes")
                    : Loc("LOCDisplayManager_OverviewGameHdrNo");
                var actionText = DescribePlanAction(plan);
                return string.Format(
                    Loc("LOCDisplayManager_OverviewGameHdrFormat"),
                    selected.Name,
                    metaText,
                    actionText);
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
            GameRefreshRateOverride refreshOverride)
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
                        gameProfiles.SetRefreshRateOverride(game, refreshOverride);
                    }

                    var first = games.FirstOrDefault();
                    if (first != null)
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            first.Name + ": " + DescribeRefreshOverride(refreshOverride),
                            Loc("LOCDisplayManager_PluginName"));
                    }
                }
            };
        }

        private GameMenuItem CreateAudioDeviceMenuItem(
            string menuSection,
            string description,
            GetGameMenuItemsArgs request,
            string deviceId)
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
                        gameProfiles.SetAssociatedAudioDeviceId(game, deviceId);
                    }

                    var first = games.FirstOrDefault();
                    if (first != null)
                    {
                        var label = string.IsNullOrWhiteSpace(deviceId)
                            ? Loc("LOCDisplayManager_GameAudioNone")
                            : description.TrimStart('✓', ' ');
                        PlayniteApi.Dialogs.ShowMessage(
                            first.Name + ": " + label,
                            Loc("LOCDisplayManager_PluginName"));
                    }
                }
            };
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

        private string DescribeRefreshOverride(GameRefreshRateOverride refreshOverride)
        {
            switch (refreshOverride)
            {
                case GameRefreshRateOverride.Native:
                    return Loc("LOCDisplayManager_GameHzNative");
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
