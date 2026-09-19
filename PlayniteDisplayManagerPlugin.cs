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
using PlayniteDisplayManager.Restore;

namespace PlayniteDisplayManager
{
    public sealed class PlayniteDisplayManagerPlugin : GenericPlugin
    {
        private readonly ILogger logger;
        private readonly HdrService hdr = new HdrService();
        private DisplayManagerSettings settings;
        private GameDisplayProfileStore gameProfiles;
        private ResourceDictionary englishFallbackResources;
        private DisplayRestoreClient restoreClient;
        private DispatcherTimer restoreHeartbeatTimer;
        private DisplaySnapshot gameSessionSnapshot;
        private Guid? activeGameId;
        private string activeGameName;

        public override Guid Id { get; } = Guid.Parse("9c2e4a71-b8d3-4f6a-a1c5-0e7d92f3b846");

        public DisplayEnumerator Displays { get; }

        public DisplayTopologyService Topology { get; }

        public HdrService Hdr => hdr;

        public GameDisplayProfileStore GameProfiles => gameProfiles;

        public PlayniteDisplayManagerPlugin(IPlayniteAPI playniteApi) : base(playniteApi)
        {
            logger = LogManager.GetLogger();
            Displays = new DisplayEnumerator();
            Topology = new DisplayTopologyService();
            gameProfiles = new GameDisplayProfileStore(GetPluginUserDataPath());
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            EnsureEnglishFallbackResources();
            ReloadSettings();
        }

        public DisplayManagerSettings Settings => settings;

        internal event EventHandler DisplaysChanged;

        public void NotifyDisplaysChanged()
        {
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
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new DisplayManagerSettingsView();
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = Loc("LOCDisplayManager_OpenSettings"),
                MenuSection = "@Display Manager",
                Action = _ => OpenSettingsView()
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var games = args?.Games?.ToList();
            if (games == null || games.Count == 0)
            {
                yield break;
            }

            var section = "Display Manager|" + Loc("LOCDisplayManager_GameMenuHdrSection");
            var current = games.Select(g => gameProfiles.GetHdrOverride(g)).ToList();

            yield return CreateHdrOverrideMenuItem(
                section,
                CheckedMenuLabel(current.All(o => o == GameHdrOverride.Inherit),
                    Loc("LOCDisplayManager_GameHdrInherit")),
                args,
                GameHdrOverride.Inherit);

            yield return CreateHdrOverrideMenuItem(
                section,
                CheckedMenuLabel(current.All(o => o == GameHdrOverride.ForceOn),
                    Loc("LOCDisplayManager_GameHdrForceOn")),
                args,
                GameHdrOverride.ForceOn);

            yield return CreateHdrOverrideMenuItem(
                section,
                CheckedMenuLabel(current.All(o => o == GameHdrOverride.ForceOff),
                    Loc("LOCDisplayManager_GameHdrForceOff")),
                args,
                GameHdrOverride.ForceOff);

            yield return CreateHdrOverrideMenuItem(
                section,
                CheckedMenuLabel(current.All(o => o == GameHdrOverride.DoNotTouch),
                    Loc("LOCDisplayManager_GameHdrDoNotTouch")),
                args,
                GameHdrOverride.DoNotTouch);
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

            var plan = PlanHdrSession(game);
            if (plan.Action == HdrSessionAction.None)
            {
                logger.Info("HDR session skipped for " + game.Name + " (" + plan.Reason + ").");
                return;
            }

            try
            {
                EndGameSession("replace-session");

                var live = Displays.GetDisplays().ToList();
                List<HdrWriteTarget> applyWrites;
                if (plan.Action == HdrSessionAction.Enable)
                {
                    applyWrites = hdr.BuildEnableWritesForPrimary(live);
                }
                else
                {
                    applyWrites = hdr.BuildForceOffWrites(live);
                }

                var offWrites = (applyWrites.Count > 0
                        ? applyWrites
                        : hdr.BuildForceOffWrites(live))
                    .Select(w => new HdrWriteTarget
                    {
                        AdapterIdLow = w.AdapterIdLow,
                        AdapterIdHigh = w.AdapterIdHigh,
                        TargetId = w.TargetId,
                        Enable = false,
                        DisplayId = w.DisplayId,
                        Name = w.Name
                    }).ToList();

                var snapshot = Topology.CaptureSnapshot();
                snapshot.HdrRestoreWrites = offWrites;
                gameSessionSnapshot = snapshot;
                activeGameId = game.Id;
                activeGameName = game.Name;

                EnsureRestoreClient();
                restoreClient.Arm(snapshot);
                StartRestoreHeartbeat();

                if (applyWrites.Count == 0)
                {
                    logger.Info("HDR plan " + plan.Action + " (" + plan.Reason +
                                "): no advanced-color-capable target; lease armed for topology only.");
                    return;
                }

                var written = hdr.ApplyHdrWrites(applyWrites, out var error);
                if (written == 0)
                {
                    logger.Warn("HDR write failed (" + plan.Reason + "): " + error);
                }
                else
                {
                    logger.Info("HDR " + plan.Action + " wrote " + written +
                                " target(s) for " + activeGameName + " (" + plan.Reason + ").");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to begin Display Manager game session.");
            }
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

        public bool GameHasHdrMetadata(Game game)
        {
            return HdrMetadataMatcher.GameIndicatesHdr(
                game,
                settings?.HdrMetadataMatchNames,
                settings?.IncludeTagsInHdrMetadataMatch ?? false);
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
