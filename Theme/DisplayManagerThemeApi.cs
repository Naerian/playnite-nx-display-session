using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PlayniteDisplayManager.Displays;

namespace PlayniteDisplayManager.Theme
{
    /// <summary>
    /// Stable theme surface via PluginSettings (SourceName DisplayManager, SettingsRoot Theme).
    /// Theme API v1 — do not rename properties without bumping ApiVersion.
    /// </summary>
    public sealed class DisplayManagerThemeApi : ObservableObject
    {
        private readonly PlayniteDisplayManagerPlugin plugin;
        private string primaryDisplayName;
        private string primaryDisplayAlias;
        private int connectedDisplayCount;
        private string hdrPolicyLabel;
        private string hdrStatusLabel;
        private bool hasHdrMetadata;
        private string selectedGameName;
        private string topPanelTooltip;
        private string displaysSummary;

        public DisplayManagerThemeApi(PlayniteDisplayManagerPlugin sourcePlugin)
        {
            plugin = sourcePlugin ?? throw new ArgumentNullException(nameof(sourcePlugin));
            Displays = new ObservableCollection<ThemeDisplayItem>();
        }

        public string ApiVersion => "1.0.0";

        public bool SupportsDisplayList => true;

        public bool SupportsHdrStatus => true;

        public bool SupportsHdrPolicy => true;

        public bool SupportsHdrMetadata => true;

        public bool SupportsTopPanel => true;

        public bool SupportsRefreshRatePolicy => true;

        public ObservableCollection<ThemeDisplayItem> Displays { get; }

        public string PrimaryDisplayName
        {
            get => primaryDisplayName;
            private set => SetValue(ref primaryDisplayName, value);
        }

        public string PrimaryDisplayAlias
        {
            get => primaryDisplayAlias;
            private set => SetValue(ref primaryDisplayAlias, value);
        }

        public int ConnectedDisplayCount
        {
            get => connectedDisplayCount;
            private set => SetValue(ref connectedDisplayCount, value);
        }

        public string HdrPolicyLabel
        {
            get => hdrPolicyLabel;
            private set => SetValue(ref hdrPolicyLabel, value);
        }

        /// <summary>Honest under ACM — typically "Unknown".</summary>
        public string HdrStatusLabel
        {
            get => hdrStatusLabel;
            private set => SetValue(ref hdrStatusLabel, value);
        }

        public bool HasHdrMetadata
        {
            get => hasHdrMetadata;
            private set => SetValue(ref hasHdrMetadata, value);
        }

        public string SelectedGameName
        {
            get => selectedGameName;
            private set => SetValue(ref selectedGameName, value);
        }

        public string TopPanelTooltip
        {
            get => topPanelTooltip;
            private set => SetValue(ref topPanelTooltip, value);
        }

        public string DisplaysSummary
        {
            get => displaysSummary;
            private set => SetValue(ref displaysSummary, value);
        }

        public bool ShowTopPanelIcon
        {
            get
            {
                var mode = plugin.Settings?.DesktopTopPanelDisplayMode ?? DesktopTopPanelDisplayMode.IconAndText;
                return mode == DesktopTopPanelDisplayMode.Icon || mode == DesktopTopPanelDisplayMode.IconAndText;
            }
        }

        public bool ShowTopPanelText
        {
            get
            {
                var mode = plugin.Settings?.DesktopTopPanelDisplayMode ?? DesktopTopPanelDisplayMode.IconAndText;
                return mode == DesktopTopPanelDisplayMode.Text || mode == DesktopTopPanelDisplayMode.IconAndText;
            }
        }

        public void Refresh()
        {
            try
            {
                var settings = plugin.Settings;
                var aliases = settings?.DisplayAliases;
                var live = plugin.Displays.GetVisibleDisplays(aliases).ToList();
                var connected = live.Where(d => d.IsConnected).ToList();
                var primary = connected.FirstOrDefault(d => d.IsPrimary) ?? connected.FirstOrDefault();

                ConnectedDisplayCount = connected.Count;
                PrimaryDisplayName = primary?.Name ?? plugin.Loc("LOCDisplayManager_StatusUnknown");
                PrimaryDisplayAlias = primary?.EffectiveName ?? PrimaryDisplayName;
                HdrPolicyLabel = plugin.GetHdrPolicyOverviewText();
                HdrStatusLabel = plugin.Loc("LOCDisplayManager_OverviewHdrUnknown");
                DisplaysSummary = string.Format(
                    plugin.Loc("LOCDisplayManager_OverviewDisplaysFormat"),
                    ConnectedDisplayCount,
                    PrimaryDisplayAlias);

                Displays.Clear();
                foreach (var display in live)
                {
                    Displays.Add(new ThemeDisplayItem
                    {
                        Id = display.Id,
                        Name = display.EffectiveName,
                        IsPrimary = display.IsPrimary,
                        IsConnected = display.IsConnected,
                        ModeLabel = BuildModeLabel(display)
                    });
                }

                var selected = plugin.GetSelectedLibraryGame();
                SelectedGameName = selected?.Name ?? string.Empty;
                HasHdrMetadata = selected != null && plugin.GameHasHdrMetadata(selected);

                TopPanelTooltip = PrimaryDisplayAlias;
                OnPropertyChanged(nameof(ApiVersion));
                OnPropertyChanged(nameof(SupportsDisplayList));
                OnPropertyChanged(nameof(SupportsHdrStatus));
                OnPropertyChanged(nameof(SupportsHdrPolicy));
                OnPropertyChanged(nameof(SupportsHdrMetadata));
                OnPropertyChanged(nameof(SupportsTopPanel));
                OnPropertyChanged(nameof(SupportsRefreshRatePolicy));
                OnPropertyChanged(nameof(ShowTopPanelIcon));
                OnPropertyChanged(nameof(ShowTopPanelText));
            }
            catch (Exception)
            {
                // Theme refresh must never break Playnite UI.
            }
        }

        private static string BuildModeLabel(DisplayInfo display)
        {
            if (display == null)
            {
                return string.Empty;
            }

            if (display.Width <= 0 || display.Height <= 0)
            {
                return display.IsConnected ? string.Empty : "offline";
            }

            var label = display.Width + "×" + display.Height;
            if (display.RefreshRateHz > 0)
            {
                label += " @" + display.RefreshRateHz.ToString("0.#") + "Hz";
            }

            return label;
        }
    }

    public sealed class ThemeDisplayItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsConnected { get; set; }
        public string ModeLabel { get; set; }
    }
}
