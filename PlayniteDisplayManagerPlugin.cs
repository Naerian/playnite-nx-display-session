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
using Playnite.SDK.Plugins;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Restore;

namespace PlayniteDisplayManager
{
    public sealed class PlayniteDisplayManagerPlugin : GenericPlugin
    {
        private readonly ILogger logger;
        private readonly HdrService hdr = new HdrService();
        private DisplayManagerSettings settings;
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

        public PlayniteDisplayManagerPlugin(IPlayniteAPI playniteApi) : base(playniteApi)
        {
            logger = LogManager.GetLogger();
            Displays = new DisplayEnumerator();
            Topology = new DisplayTopologyService();
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

        private void BeginGameSession(Playnite.SDK.Models.Game game)
        {
            if (game == null || settings == null)
            {
                return;
            }

            if (settings.GlobalHdrPolicy != GlobalHdrPolicy.OnForAllGames)
            {
                // Policy 3 (metadata) arrives in step 7; DoNotManage skips.
                return;
            }

            try
            {
                EndGameSession("replace-session");

                var live = Displays.GetDisplays().ToList();
                var enableWrites = hdr.BuildEnableWritesForPrimary(live);
                var offWrites = enableWrites.Select(w => new HdrWriteTarget
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

                if (enableWrites.Count == 0)
                {
                    logger.Info("HDR policy OnForAllGames: primary display does not report advanced color support; lease armed for topology only.");
                    return;
                }

                var written = hdr.ApplyHdrWrites(enableWrites, out var error);
                if (written == 0)
                {
                    logger.Warn("HDR enable write failed: " + error);
                }
                else
                {
                    logger.Info("HDR enabled by write for " + written + " target(s) (game: " + activeGameName + ").");
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
