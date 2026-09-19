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
using PlayniteDisplayManager.Restore;

namespace PlayniteDisplayManager
{
    public sealed class PlayniteDisplayManagerPlugin : GenericPlugin
    {
        private readonly ILogger logger;
        private DisplayManagerSettings settings;
        private ResourceDictionary englishFallbackResources;
        private DisplayRestoreClient restoreClient;
        private DispatcherTimer restoreHeartbeatTimer;

        public override Guid Id { get; } = Guid.Parse("9c2e4a71-b8d3-4f6a-a1c5-0e7d92f3b846");

        public DisplayEnumerator Displays { get; }

        public DisplayTopologyService Topology { get; }

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

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            StopRestoreHeartbeat();
            if (restoreClient != null)
            {
                // Graceful unload: disarm without forcing host restore; dispose sends SHUTDOWN.
                try { restoreClient.Disarm(); } catch { /* ignore */ }
                restoreClient.Dispose();
                restoreClient = null;
            }
        }

        /// <summary>
        /// Captures the current topology and arms RestoreHost (fake apply for lease testing).
        /// </summary>
        public string ArmRestoreLeaseForTest()
        {
            var snapshot = Topology.CaptureSnapshot();
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

        public bool TryRestoreSnapshotNow(out string error)
        {
            var snapshot = Topology.CaptureSnapshot();
            return Topology.TryRestoreSnapshot(snapshot, out error);
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
