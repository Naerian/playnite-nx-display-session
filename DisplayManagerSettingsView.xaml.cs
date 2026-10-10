using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Profiles;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Resolution;

namespace PlayniteDisplayManager
{
    public partial class DisplayManagerSettingsView : UserControl
    {
        private readonly bool themeStandaloneWindow;
        private ScrollViewer hostScrollViewer;
        private Window hostWindow;
        private bool settingsWindowPlacementApplied;
        private bool settingsWindowPlacementSaved;
        private PlayniteDisplayManagerPlugin subscribedPlugin;
        private DisplaySnapshot topologyTrialSnapshot;
        private DispatcherTimer topologyTrialTimer;
        private int topologyTrialSecondsLeft;
        private bool syncingRefreshRadios;
        private bool syncingResolutionRadios;
        private bool syncingHdrPolicy;
        private bool syncingHdrMetadata;
        private bool syncingTopologyTarget;
        private int topologySyncDepth;
        private bool syncingLaunchMode;
        private bool settingsUiRefreshQueued;
        private bool suppressAppearancePresetChange;
        private ApplicationMode editingLaunchMode = ApplicationMode.Desktop;
        private int? cachedNativeHdrEnabledCount;
        private string cachedRatesDisplayId;
        private IReadOnlyList<double> cachedRates;
        private string cachedModesDisplayId;
        private IReadOnlyList<ResolutionMode> cachedModes;
        private readonly Dictionary<string, bool> hdrSupportCache =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private const string WindowsPlayDisplayChoiceId = "__windows_primary__";

        private sealed class PlayDisplayChoice
        {
            public string Id { get; set; }
            public string EffectiveName { get; set; }
        }

        /// <summary>
        /// ComboBox row for Exact resolution: group header, real mode, or stale/unavailable mode.
        /// Flat list avoids WPF GroupStyle + themed ComboBox templates (headers without items).
        /// </summary>
        private sealed class ResolutionExactItem
        {
            public bool IsHeader { get; set; }

            public bool IsUnavailable { get; set; }

            public string Label { get; set; }

            public ResolutionMode Mode { get; set; }

            public int Width => Mode?.Width ?? 0;

            public int Height => Mode?.Height ?? 0;
        }

        private sealed class RefreshExactItem
        {
            public bool IsUnavailable { get; set; }

            public string Label { get; set; }

            public double Hz { get; set; }
        }

        private sealed class NamedChoice
        {
            public string Value { get; set; }
            public string DisplayName { get; set; }
        }

        public DisplayManagerSettingsView() : this(false)
        {
        }

        public DisplayManagerSettingsView(bool themeStandaloneWindow)
        {
            this.themeStandaloneWindow = themeStandaloneWindow;
            InitializeComponent();
            AboutVersionText.Text = string.Format(
                TryFindResource("LOCDisplayManager_VersionAuthorFormat") as string ?? "Display Manager {0} · Narian",
                GetInstalledVersion());

            DataContextChanged += (_, __) => ScheduleSettingsUiRefresh();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            ApplyPreferredWindowSize();
            AttachToHost();
            Dispatcher.BeginInvoke(new Action(ApplyPreferredWindowSize), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(AttachToHost), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(ApplyPreferredWindowSize), DispatcherPriority.ApplicationIdle);
            Dispatcher.BeginInvoke(new Action(AttachToHost), DispatcherPriority.ApplicationIdle);
            Dispatcher.BeginInvoke(new Action(FillSelectedContentHosts), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(FillSelectedContentHosts), DispatcherPriority.ApplicationIdle);
            ScheduleSettingsUiRefresh();
        }

        private void ScheduleSettingsUiRefresh()
        {
            if (settingsUiRefreshQueued)
            {
                return;
            }

            settingsUiRefreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                settingsUiRefreshQueued = false;
                RefreshAllSettingsUi();
            }), DispatcherPriority.Loaded);
        }

        private void RefreshAllSettingsUi()
        {
            var sw = Stopwatch.StartNew();
            SubscribeDisplaysChanged();
            InvalidateDisplayQueryCaches();
            ApplyAppearancePreset();
            BindAppearancePresetSelector();
            RebuildDisplayCards();
            RefreshDisplayProfilesUi();
            SyncDesktopAccessControls();
            SyncFullscreenRelocateControl();
            SyncFullscreenDpiControl();
            SyncLoggingControls();
            // Game/platform profile editors are heavy (ComboBoxes × modes per row).
            // Build them only when those tabs are visible — not on every settings open.
            MaybeRebuildVisibleProfileEditors();
            sw.Stop();

            var settings = DataContext as DisplayManagerSettings;
            var gameProfiles = settings?.AvailableGameProfiles?.Count ?? 0;
            var platformProfiles = settings?.AvailablePlatformProfiles?.Count ?? 0;
            settings?.Plugin?.LogInfo(
                $"Settings UI refresh: {sw.ElapsedMilliseconds} ms · gameProfiles={gameProfiles} · platformProfiles={platformProfiles}");
        }

        private void InvalidateDisplayQueryCaches()
        {
            cachedNativeHdrEnabledCount = null;
            cachedRatesDisplayId = null;
            cachedRates = null;
            cachedModesDisplayId = null;
            cachedModes = null;
            hdrSupportCache.Clear();
        }

        private int GetCachedNativeHdrEnabledCount(PlayniteDisplayManagerPlugin plugin)
        {
            if (plugin == null)
            {
                return 0;
            }

            if (!cachedNativeHdrEnabledCount.HasValue)
            {
                cachedNativeHdrEnabledCount = plugin.CountNativeHdrEnabledGames();
            }

            return cachedNativeHdrEnabledCount.Value;
        }

        private IReadOnlyList<double> GetCachedAvailableRates(DisplayManagerSettings settings, DisplayInfo display)
        {
            var key = display?.Id ?? string.Empty;
            if (cachedRates != null
                && string.Equals(cachedRatesDisplayId, key, StringComparison.OrdinalIgnoreCase))
            {
                return cachedRates;
            }

            cachedRatesDisplayId = key;
            cachedRates = settings?.Plugin?.RefreshRates?.GetAvailableRates(display)
                ?? (IReadOnlyList<double>)Array.Empty<double>();
            return cachedRates;
        }

        private IReadOnlyList<ResolutionMode> GetCachedAvailableModes(
            DisplayManagerSettings settings,
            DisplayInfo display)
        {
            var key = display?.Id ?? string.Empty;
            if (cachedModes != null
                && string.Equals(cachedModesDisplayId, key, StringComparison.OrdinalIgnoreCase))
            {
                return cachedModes;
            }

            cachedModesDisplayId = key;
            cachedModes = settings?.Plugin?.Resolutions?.GetAvailableModes(display)
                ?? (IReadOnlyList<ResolutionMode>)Array.Empty<ResolutionMode>();
            return cachedModes;
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            PersistSettingsWindowPlacement();
            StopTopologyTrialTimer(restore: true);
            UnsubscribeDisplaysChanged();
            DetachFromHost();
        }

        private void SubscribeDisplaysChanged()
        {
            UnsubscribeDisplaysChanged();
            if (DataContext is DisplayManagerSettings settings && settings.Plugin != null)
            {
                subscribedPlugin = settings.Plugin;
                subscribedPlugin.DisplaysChanged += OnDisplaysChanged;
            }
        }

        private void UnsubscribeDisplaysChanged()
        {
            if (subscribedPlugin != null)
            {
                subscribedPlugin.DisplaysChanged -= OnDisplaysChanged;
                subscribedPlugin = null;
            }
        }

        private void OnDisplaysChanged(object sender, EventArgs args)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ScheduleSettingsUiRefresh));
                return;
            }

            ScheduleSettingsUiRefresh();
        }

        private void ApplyAppearancePreset()
        {
            var settings = DataContext as DisplayManagerSettings;
            var preset = settings != null
                ? settings.AppearancePreset
                : SettingsAppearance.Default;
            SettingsAppearance.Apply(this, preset);

            if (themeStandaloneWindow)
            {
                SettingsAppearance.ApplyWindow(Window.GetWindow(this), preset);
            }

            SyncAppearancePresetSelector(preset);
        }

        private void RootTabsSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            // Nested Desktop/Fullscreen TabControls bubble SelectionChanged; ignore those
            // or every mode sync rebuilds all game-profile ComboBoxes (very expensive).
            if (!ReferenceEquals(args.Source, sender))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                FillSelectedContentHosts();
                MaybeSyncLaunchRateAndResolutionEditors();
                MaybeRebuildVisibleProfileEditors();
            }), DispatcherPriority.Loaded);
        }

        private void MaybeRebuildVisibleProfileEditors()
        {
            if (IsNestedTabSelected(RootTabs, "GameProfiles"))
            {
                RebuildGameProfileRows();
            }

            if (IsNestedTabSelected(RootTabs, "PlatformProfiles"))
            {
                RebuildPlatformProfileRows();
            }
        }

        private static bool IsNestedTabSelected(TabControl root, string tag)
        {
            if (root == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            var selected = root.SelectedItem as TabItem;
            if (selected == null)
            {
                return false;
            }

            if (string.Equals(selected.Tag as string, tag, StringComparison.Ordinal))
            {
                return true;
            }

            return IsNestedTabSelectedInContent(selected.Content, tag);
        }

        private static bool IsNestedTabSelectedInContent(object content, string tag)
        {
            if (content is TabControl nestedTabs)
            {
                return IsNestedTabSelected(nestedTabs, tag);
            }

            if (!(content is DependencyObject node))
            {
                return false;
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (IsNestedTabSelectedInContent(child, tag))
                {
                    return true;
                }
            }

            return false;
        }

        private void AttachToHost()
        {
            DetachFromHost();
            hostScrollViewer = FindAncestorScrollViewer();
            if (hostScrollViewer != null)
            {
                hostScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                hostScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                hostScrollViewer.SizeChanged += OnHostSizeChanged;
            }

            hostWindow = Window.GetWindow(this);
            if (hostWindow != null)
            {
                hostWindow.SizeChanged += OnHostSizeChanged;
                hostWindow.Closing -= OnHostWindowClosing;
                hostWindow.Closing += OnHostWindowClosing;
            }

            ApplyViewportSize();
        }

        private void DetachFromHost()
        {
            if (hostScrollViewer != null)
            {
                hostScrollViewer.SizeChanged -= OnHostSizeChanged;
                hostScrollViewer = null;
            }

            if (hostWindow != null)
            {
                hostWindow.SizeChanged -= OnHostSizeChanged;
                hostWindow.Closing -= OnHostWindowClosing;
                hostWindow = null;
            }
        }

        private void OnHostWindowClosing(object sender, System.ComponentModel.CancelEventArgs args)
        {
            PersistSettingsWindowPlacement();
        }

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs args)
        {
            ApplyViewportSize();
        }

        private void FillSelectedContentHosts()
        {
            StretchSelectedContent(this);
        }

        private static void StretchSelectedContent(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var presenter = child as ContentPresenter;
                if (presenter != null && presenter.Name == "PART_SelectedContentHost")
                {
                    presenter.HorizontalAlignment = HorizontalAlignment.Stretch;
                    presenter.VerticalAlignment = VerticalAlignment.Stretch;
                    var content = presenter.Content as FrameworkElement;
                    if (content == null && VisualTreeHelper.GetChildrenCount(presenter) > 0)
                    {
                        content = VisualTreeHelper.GetChild(presenter, 0) as FrameworkElement;
                    }

                    if (content != null)
                    {
                        content.HorizontalAlignment = HorizontalAlignment.Stretch;
                        content.VerticalAlignment = VerticalAlignment.Stretch;
                        content.ClearValue(WidthProperty);
                        content.ClearValue(HeightProperty);
                    }
                }

                StretchSelectedContent(child);
            }
        }

        private void ApplyViewportSize()
        {
            double width = 0;
            double height = 0;
            if (hostScrollViewer != null)
            {
                width = hostScrollViewer.ViewportWidth > 8
                    ? hostScrollViewer.ViewportWidth
                    : hostScrollViewer.ActualWidth;
                height = hostScrollViewer.ViewportHeight > 8
                    ? hostScrollViewer.ViewportHeight
                    : hostScrollViewer.ActualHeight;
            }

            if (width < 8 || height < 8)
            {
                var slot = FindWindowGridSlot();
                if (slot.Width > 8)
                {
                    width = slot.Width;
                }
                if (slot.Height > 8)
                {
                    height = slot.Height;
                }
            }

            if ((width < 8 || height < 8) && hostWindow != null)
            {
                var content = hostWindow.Content as FrameworkElement;
                if (content != null)
                {
                    if (width < 8)
                    {
                        width = content.ActualWidth;
                    }
                    if (height < 8)
                    {
                        height = content.ActualHeight;
                    }
                }
            }

            if (width > 8 && Math.Abs(Width - width) > 1)
            {
                Width = width;
            }

            if (height > 8 && Math.Abs(Height - height) > 1)
            {
                Height = height;
            }

            FillSelectedContentHosts();
        }

        private Size FindWindowGridSlot()
        {
            for (var parent = VisualTreeHelper.GetParent(this);
                 parent != null;
                 parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is Window)
                {
                    break;
                }

                var grid = parent as Grid;
                if (grid == null || grid.RowDefinitions.Count < 2 || grid.ActualWidth < 400)
                {
                    continue;
                }

                var rowHeight = grid.RowDefinitions[0].ActualHeight;
                if (rowHeight > 200)
                {
                    return new Size(grid.ActualWidth, rowHeight);
                }
            }

            return new Size(0, 0);
        }

        private ScrollViewer FindAncestorScrollViewer()
        {
            for (var parent = VisualTreeHelper.GetParent(this);
                 parent != null;
                 parent = VisualTreeHelper.GetParent(parent))
            {
                var scrollViewer = parent as ScrollViewer;
                if (scrollViewer != null)
                {
                    return scrollViewer;
                }

                if (parent is Window)
                {
                    return null;
                }
            }

            return null;
        }

        private void ApplyPreferredWindowSize()
        {
            var window = Window.GetWindow(this);
            if (window == null || settingsWindowPlacementApplied)
            {
                return;
            }

            settingsWindowPlacementApplied = true;
            window.SizeToContent = SizeToContent.Manual;
            if (window.ResizeMode == ResizeMode.NoResize || window.ResizeMode == ResizeMode.CanMinimize)
            {
                window.ResizeMode = ResizeMode.CanResize;
            }

            if (window.MinWidth < 1000)
            {
                window.MinWidth = 1000;
            }

            if (window.MinHeight < 700)
            {
                window.MinHeight = 700;
            }

            var placement = LoadSettingsWindowPlacement();
            if (placement != null)
            {
                window.Width = placement.Width;
                window.Height = placement.Height;
                if (SettingsWindowPlacementStore.IsOnVirtualScreen(
                        placement.Left,
                        placement.Top,
                        placement.Width,
                        placement.Height))
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = placement.Left;
                    window.Top = placement.Top;
                }

                if (placement.Maximized)
                {
                    window.WindowState = WindowState.Maximized;
                }

                return;
            }

            if (window.ActualWidth < 1100 && window.Width < 1100)
            {
                window.Width = 1100;
            }

            if (window.ActualHeight < 780 && window.Height < 780)
            {
                window.Height = 780;
            }
        }

        private SettingsWindowPlacement LoadSettingsWindowPlacement()
        {
            var settings = DataContext as DisplayManagerSettings;
            var path = settings?.Plugin?.UserDataPath;
            return SettingsWindowPlacementStore.Load(path);
        }

        private void PersistSettingsWindowPlacement()
        {
            if (settingsWindowPlacementSaved)
            {
                return;
            }

            var window = hostWindow ?? Window.GetWindow(this);
            var settings = DataContext as DisplayManagerSettings;
            var path = settings?.Plugin?.UserDataPath;
            if (window == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var bounds = window.WindowState == WindowState.Maximized
                    ? window.RestoreBounds
                    : new Rect(window.Left, window.Top, window.ActualWidth > 0 ? window.ActualWidth : window.Width,
                        window.ActualHeight > 0 ? window.ActualHeight : window.Height);

                var width = bounds.Width > 0 ? bounds.Width : window.Width;
                var height = bounds.Height > 0 ? bounds.Height : window.Height;
                var left = bounds.Width > 0 ? bounds.Left : window.Left;
                var top = bounds.Height > 0 ? bounds.Top : window.Top;

                var placement = new SettingsWindowPlacement
                {
                    Width = width,
                    Height = height,
                    Left = left,
                    Top = top,
                    Maximized = window.WindowState == WindowState.Maximized
                };

                if (!SettingsWindowPlacementStore.IsUsable(placement))
                {
                    return;
                }

                SettingsWindowPlacementStore.Save(path, placement);
                settingsWindowPlacementSaved = true;
            }
            catch
            {
                // Best-effort UI chrome persistence only.
            }
        }

        private void BindAppearancePresetSelector()
        {
            if (AppearancePresetSelector == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            suppressAppearancePresetChange = true;
            try
            {
                AppearancePresetSelector.ItemsSource = settings != null
                    ? settings.AppearancePresetOptions
                    : null;
                SyncAppearancePresetSelector(settings != null
                    ? settings.AppearancePreset
                    : SettingsAppearance.Default);
            }
            finally
            {
                suppressAppearancePresetChange = false;
            }
        }

        private void SyncAppearancePresetSelector(string preset)
        {
            if (AppearancePresetSelector == null)
            {
                return;
            }

            var normalized = SettingsAppearance.Normalize(preset);
            if (Equals(AppearancePresetSelector.SelectedValue, normalized))
            {
                return;
            }

            suppressAppearancePresetChange = true;
            try
            {
                AppearancePresetSelector.SelectedValue = normalized;
            }
            finally
            {
                suppressAppearancePresetChange = false;
            }
        }

        private void AppearancePresetSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressAppearancePresetChange)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            var preset = AppearancePresetSelector?.SelectedValue as string;
            if (settings == null || string.IsNullOrWhiteSpace(preset))
            {
                return;
            }

            settings.AppearancePreset = preset;
            ApplyAppearancePreset();
        }

        private void RefreshOverview_OnClick(object sender, RoutedEventArgs e)
        {
            RefreshDisplaysInternal();
        }

        private void RefreshDisplays_OnClick(object sender, RoutedEventArgs e)
        {
            RefreshDisplaysInternal();
        }

        private void ArmRestoreLease_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (plugin == null)
            {
                return;
            }

            try
            {
                var path = plugin.ArmRestoreLeaseForTest();
                RestoreLeaseStatusText.Text = string.Format(
                    TryFindResource("LOCDisplayManager_RestoreLeaseArmedFormat") as string
                    ?? "Lease armed. Snapshot: {0}. Kill Playnite (or stop heartbeats) to verify host restore. Log: %TEMP%\\PlayniteDisplayManager-RestoreHost.log",
                    path);
            }
            catch (Exception ex)
            {
                RestoreLeaseStatusText.Text = ex.Message;
            }
        }

        private void DisarmRestoreLease_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            settings?.Plugin?.DisarmRestoreLease();
            RestoreLeaseStatusText.Text = TryFindResource("LOCDisplayManager_RestoreLeaseDisarmed") as string
                ?? "Lease disarmed.";
        }

        private void RefreshDisplaysInternal()
        {
            var settings = DataContext as DisplayManagerSettings;
            settings?.RefreshDisplays();
            settings?.Plugin?.NotifyDisplaysChanged();
            ScheduleSettingsUiRefresh();
        }

        private DisplayProfile GetEditingLaunchProfile(DisplayManagerSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            settings.MigrateDisplayProfilesPublic();
            return settings.GetDefaultDisplayProfileForMode(editingLaunchMode);
        }

        private void RefreshDisplayProfilesUi()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            settings.MigrateDisplayProfilesPublic();
            if (MissingDisplayPolicyBox != null)
            {
                MissingDisplayPolicyBox.ItemsSource = new[]
                {
                    new NamedChoice
                    {
                        Value = nameof(MissingDisplayPolicy.UseWindowsPrimary),
                        DisplayName = TryFindResource("LOCDisplayManager_MissingDisplayWindows") as string
                            ?? "Use Windows primary"
                    },
                    new NamedChoice
                    {
                        Value = nameof(MissingDisplayPolicy.UseFallbackDisplay),
                        DisplayName = TryFindResource("LOCDisplayManager_MissingDisplayFallbackChoice") as string
                            ?? "Use fallback display"
                    },
                    new NamedChoice
                    {
                        Value = nameof(MissingDisplayPolicy.NotifyAndContinue),
                        DisplayName = TryFindResource("LOCDisplayManager_MissingDisplayNotify") as string
                            ?? "Notify and continue"
                    }
                };
            }

            SyncLaunchModeTabs();
            RefreshPlayDisplayBoxes();
            RefreshLaunchModeDependentUi();
        }

        private IEnumerable<TabControl> LaunchModeTabControls()
        {
            if (HdrLaunchModeTabs != null)
            {
                yield return HdrLaunchModeTabs;
            }

            if (RefreshLaunchModeTabs != null)
            {
                yield return RefreshLaunchModeTabs;
            }

            if (ResolutionLaunchModeTabs != null)
            {
                yield return ResolutionLaunchModeTabs;
            }

            if (MissingLaunchModeTabs != null)
            {
                yield return MissingLaunchModeTabs;
            }
        }

        private void SyncLaunchModeTabs()
        {
            var index = editingLaunchMode == ApplicationMode.Fullscreen ? 1 : 0;
            syncingLaunchMode = true;
            try
            {
                foreach (var tabs in LaunchModeTabControls())
                {
                    if (tabs.SelectedIndex != index)
                    {
                        tabs.SelectedIndex = index;
                    }
                }
            }
            finally
            {
                syncingLaunchMode = false;
            }
        }

        private void LaunchModeTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingLaunchMode)
            {
                return;
            }

            var tabs = sender as TabControl;
            if (tabs == null || !ReferenceEquals(e.Source, tabs))
            {
                return;
            }

            var selected = tabs.SelectedItem as TabItem;
            var tag = selected?.Tag as string;
            var next = string.Equals(tag, "Fullscreen", StringComparison.OrdinalIgnoreCase)
                ? ApplicationMode.Fullscreen
                : ApplicationMode.Desktop;
            if (next == editingLaunchMode)
            {
                SyncLaunchModeTabs();
                return;
            }

            editingLaunchMode = next;
            SyncLaunchModeTabs();
            RefreshLaunchModeDependentUi();
        }

        private void RefreshLaunchModeDependentUi()
        {
            RefreshMissingDisplayAndTurnOffUi();
            SyncHdrPolicyRadios();
            SyncHdrMetadataControls();
            SyncHdrCapabilityUi();
            SyncNativeHdrMigrationStatus();
            // CRT/CRU RAWMODE enumeration is expensive — only when those pages are open.
            MaybeSyncLaunchRateAndResolutionEditors();
            UpdateOverview();
        }

        private void MaybeSyncLaunchRateAndResolutionEditors()
        {
            if (IsNestedTabSelected(RootTabs, "LaunchRefresh"))
            {
                SyncRefreshRateRadios();
            }

            if (IsNestedTabSelected(RootTabs, "LaunchResolution"))
            {
                SyncResolutionRadios();
            }
        }

        private List<PlayDisplayChoice> BuildPlayDisplayChoices(DisplayManagerSettings settings)
        {
            var connected = settings?.AvailableDisplays?
                .Where(d => d.IsConnected)
                .ToList() ?? new List<DisplayInfo>();

            var choices = new List<PlayDisplayChoice>
            {
                new PlayDisplayChoice
                {
                    Id = WindowsPlayDisplayChoiceId,
                    EffectiveName = TryFindResource("LOCDisplayManager_PlayDisplayWindowsDefault") as string
                        ?? "Keep Windows default"
                }
            };
            choices.AddRange(connected.Select(d => new PlayDisplayChoice
            {
                Id = d.Id,
                EffectiveName = d.EffectiveName
            }));
            return choices;
        }

        private static void SelectPlayDisplayChoice(
            ComboBox box,
            IList<PlayDisplayChoice> choices,
            string preferredId)
        {
            if (box == null)
            {
                return;
            }

            box.ItemsSource = choices;
            if (string.IsNullOrWhiteSpace(preferredId)
                || !choices.Any(c => string.Equals(c.Id, preferredId, StringComparison.OrdinalIgnoreCase)))
            {
                box.SelectedValue = WindowsPlayDisplayChoiceId;
            }
            else
            {
                box.SelectedValue = preferredId;
            }
        }

        private void RefreshPlayDisplayBoxes()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            settings.MigrateDisplayProfilesPublic();
            var choices = BuildPlayDisplayChoices(settings);
            var desktop = settings.GetDefaultDisplayProfileForMode(ApplicationMode.Desktop);
            var fullscreen = settings.GetDefaultDisplayProfileForMode(ApplicationMode.Fullscreen);

            BeginTopologySync();
            SelectPlayDisplayChoice(DesktopPlayDisplayBox, choices, desktop?.PreferredPlayDisplayId);
            SelectPlayDisplayChoice(FullscreenPlayDisplayBox, choices, fullscreen?.PreferredPlayDisplayId);
            EndTopologySyncDeferred();

            SyncHdrCapabilityUi();
        }

        private void RefreshMissingDisplayAndTurnOffUi()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            var profile = GetEditingLaunchProfile(settings);
            var choices = BuildPlayDisplayChoices(settings);

            BeginTopologySync();
            if (MissingDisplayFallbackBox != null)
            {
                MissingDisplayFallbackBox.ItemsSource = choices;
                var fallback = profile?.FallbackDisplayId;
                if (!string.IsNullOrWhiteSpace(fallback)
                    && choices.Any(c => string.Equals(c.Id, fallback, StringComparison.OrdinalIgnoreCase)))
                {
                    MissingDisplayFallbackBox.SelectedValue = fallback;
                }
                else
                {
                    MissingDisplayFallbackBox.SelectedValue = WindowsPlayDisplayChoiceId;
                }
            }

            if (MissingDisplayPolicyBox != null && profile != null)
            {
                MissingDisplayPolicyBox.SelectedValue = profile.MissingDisplayPolicy.ToString();
            }

            if (TopologyTurnOffOthersCheck != null)
            {
                TopologyTurnOffOthersCheck.IsChecked = profile?.TurnOffOtherDisplays == true;
            }

            EndTopologySyncDeferred();
        }

        private void BeginTopologySync()
        {
            topologySyncDepth++;
            syncingTopologyTarget = true;
        }

        private void EndTopologySyncDeferred()
        {
            // SelectionChanged can raise after ItemsSource assignment; keep the guard until input idle.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (topologySyncDepth > 0)
                {
                    topologySyncDepth--;
                }

                if (topologySyncDepth == 0)
                {
                    syncingTopologyTarget = false;
                }
            }), DispatcherPriority.Input);
        }

        private void PersistDefaultDisplayProfile(Action<DisplayProfile> mutate)
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingLaunchProfile(settings);
            if (settings == null || profile == null || mutate == null)
            {
                return;
            }

            mutate(profile);
            if (editingLaunchMode == ApplicationMode.Desktop)
            {
                settings.SyncLegacyFieldsFromDefaultTopology();
            }
        }

        private void ApplyPlayDisplaySelection(ApplicationMode mode, string selectedId)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || string.IsNullOrWhiteSpace(selectedId))
            {
                return;
            }

            var profile = settings.GetDefaultDisplayProfileForMode(mode);
            if (profile == null)
            {
                return;
            }

            profile.PreferredPlayDisplayId =
                string.Equals(selectedId, WindowsPlayDisplayChoiceId, StringComparison.Ordinal)
                    ? null
                    : selectedId;

            if (mode == ApplicationMode.Desktop)
            {
                settings.SyncLegacyFieldsFromDefaultDisplayProfile();
            }

            InvalidateDisplayQueryCaches();
            settings.Plugin?.Resolutions?.ClearCache();
            settings.Plugin?.RefreshRates?.ClearCache();
            SanitizeGlobalOverridesForPlayDisplay(settings, mode);
            RebuildDisplayCards();
            if (mode == editingLaunchMode)
            {
                SyncHdrCapabilityUi();
                MaybeSyncLaunchRateAndResolutionEditors();
            }

            UpdateOverview();
        }

        private void DesktopPlayDisplayBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingTopologyTarget)
            {
                return;
            }

            ApplyPlayDisplaySelection(ApplicationMode.Desktop, DesktopPlayDisplayBox?.SelectedValue as string);
        }

        private void FullscreenPlayDisplayBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingTopologyTarget)
            {
                return;
            }

            ApplyPlayDisplaySelection(ApplicationMode.Fullscreen, FullscreenPlayDisplayBox?.SelectedValue as string);
        }

        private void SanitizeGlobalOverridesForPlayDisplay(DisplayManagerSettings settings, ApplicationMode mode)
        {
            if (settings == null)
            {
                return;
            }

            var connected = settings.AvailableDisplays?
                .Where(d => d.IsConnected)
                .ToList() ?? new List<DisplayInfo>();
            var target = ResolvePreferredPlayDisplayForMode(settings, connected, mode);
            if (target == null)
            {
                return;
            }

            settings.GetPreferredResolutionForMode(mode, out var width, out var height);
            var result = DisplayOverrideSanitizer.SanitizeGlobalSettings(
                width,
                height,
                settings.GetResolutionPolicyForMode(mode),
                settings.GetPreferredRefreshRateHzForMode(mode),
                settings.GetRefreshRatePolicyForMode(mode),
                target,
                settings.Plugin?.Resolutions,
                settings.Plugin?.RefreshRates,
                out var newResolutionPolicy,
                out var newPreferredWidth,
                out var newPreferredHeight,
                out var newRefreshPolicy,
                out var newPreferredHz);

            if (!result.Changed)
            {
                return;
            }

            settings.SetResolutionPolicyForMode(mode, newResolutionPolicy);
            settings.SetPreferredResolutionForMode(mode, newPreferredWidth, newPreferredHeight);
            settings.SetRefreshRatePolicyForMode(mode, newRefreshPolicy);
            settings.SetPreferredRefreshRateHzForMode(mode, newPreferredHz);
            settings.Plugin?.ShowOverrideResetMessage(result, useNativeDefault: true);
        }

        private void TopologyTurnOffOthersCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            if (syncingTopologyTarget || TopologyTurnOffOthersCheck == null)
            {
                return;
            }

            PersistDefaultDisplayProfile(profile =>
                profile.TurnOffOtherDisplays = TopologyTurnOffOthersCheck.IsChecked == true);
        }

        private void MissingDisplayPolicyBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingTopologyTarget || MissingDisplayPolicyBox?.SelectedValue == null)
            {
                return;
            }

            if (Enum.TryParse(MissingDisplayPolicyBox.SelectedValue as string, out MissingDisplayPolicy policy))
            {
                PersistDefaultDisplayProfile(profile => profile.MissingDisplayPolicy = policy);
            }
        }

        private void MissingDisplayFallbackBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingTopologyTarget)
            {
                return;
            }

            var selectedId = MissingDisplayFallbackBox?.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(selectedId))
            {
                return;
            }

            PersistDefaultDisplayProfile(profile =>
            {
                if (string.Equals(selectedId, WindowsPlayDisplayChoiceId, StringComparison.Ordinal))
                {
                    profile.FallbackDisplayId = null;
                }
                else
                {
                    profile.FallbackDisplayId = selectedId;
                }
            });
        }

        private void RelocateFullscreenCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || RelocateFullscreenCheck == null)
            {
                return;
            }

            settings.RelocatePlayniteFullscreenAfterRestore = RelocateFullscreenCheck.IsChecked == true;
        }

        private void SyncFullscreenRelocateControl()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || RelocateFullscreenCheck == null)
            {
                return;
            }

            RelocateFullscreenCheck.IsChecked = settings.RelocatePlayniteFullscreenAfterRestore;
        }

        private void Force100PercentScaleCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || Force100PercentScaleCheck == null)
            {
                return;
            }

            settings.Force100PercentScaleInFullscreen = Force100PercentScaleCheck.IsChecked == true;
        }

        private void SyncFullscreenDpiControl()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || Force100PercentScaleCheck == null)
            {
                return;
            }

            Force100PercentScaleCheck.IsChecked = settings.Force100PercentScaleInFullscreen;
        }

        private void SyncLoggingControls()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || VerboseLoggingCheck == null)
            {
                return;
            }

            VerboseLoggingCheck.IsChecked = settings.EnableVerboseLogging;
        }

        private void VerboseLoggingCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || VerboseLoggingCheck == null)
            {
                return;
            }

            settings.EnableVerboseLogging = VerboseLoggingCheck.IsChecked == true;
        }

        private void OpenSupportLogFile_OnClick(object sender, RoutedEventArgs e)
        {
            var plugin = (DataContext as DisplayManagerSettings)?.Plugin;
            if (plugin == null)
            {
                return;
            }

            if (!plugin.TryOpenSupportLogFile(out var error))
            {
                MessageBox.Show(
                    (TryFindResource("LOCDisplayManager_LoggingOpenFailed") as string
                        ?? "Could not open the debug log.") +
                    (string.IsNullOrWhiteSpace(error) ? string.Empty : "\n\n" + error),
                    "Display Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ClearSupportLog_OnClick(object sender, RoutedEventArgs e)
        {
            var plugin = (DataContext as DisplayManagerSettings)?.Plugin;
            if (plugin == null)
            {
                return;
            }

            var confirm = TryFindResource("LOCDisplayManager_LoggingClearConfirm") as string
                ?? "Clear the debug log? Only the header will remain.";
            if (MessageBox.Show(
                    confirm,
                    "Display Manager",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            if (plugin.TryClearSupportLog(out var error))
            {
                MessageBox.Show(
                    TryFindResource("LOCDisplayManager_LoggingCleared") as string
                        ?? "Debug log cleared.",
                    "Display Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    (TryFindResource("LOCDisplayManager_LoggingClearFailed") as string
                        ?? "Could not clear the debug log.") +
                    (string.IsNullOrWhiteSpace(error) ? string.Empty : "\n\n" + error),
                    "Display Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void TopologyTrialApply_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (plugin == null)
            {
                return;
            }

            var targetId = DesktopPlayDisplayBox?.SelectedValue as string;
            var makePrimary = true;
            if (string.IsNullOrWhiteSpace(targetId)
                || string.Equals(targetId, WindowsPlayDisplayChoiceId, StringComparison.Ordinal))
            {
                var windowsPrimary = settings.AvailableDisplays?
                    .FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                    ?? settings.AvailableDisplays?.FirstOrDefault(d => d.IsConnected);
                if (windowsPrimary == null)
                {
                    TopologyTrialStatusText.Text = TryFindResource("LOCDisplayManager_TopologyTrialNoTarget") as string
                        ?? "Select a connected display first.";
                    return;
                }

                targetId = windowsPrimary.Id;
                makePrimary = false;
            }

            var desktopProfile = settings.GetDefaultDisplayProfileForMode(ApplicationMode.Desktop);
            var turnOffOthers = desktopProfile?.TurnOffOtherDisplays == true;
            if (!makePrimary && !turnOffOthers)
            {
                TopologyTrialStatusText.Text = TryFindResource("LOCDisplayManager_TopologyTrialNothing") as string
                    ?? "Select the primary display for games first.";
                return;
            }

            if (turnOffOthers)
            {
                var confirm = MessageBox.Show(
                    TryFindResource("LOCDisplayManager_TopologyTurnOffConfirm") as string
                    ?? "Other displays will turn off for a few seconds, then restore. Continue?",
                    "Display Manager",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var apply = plugin.ApplyTopologyWithLease(new DisplayTopologyRequest
            {
                TargetDisplayId = targetId,
                MakePrimary = makePrimary,
                TurnOffOtherDisplays = turnOffOthers
            });

            if (!apply.Success)
            {
                TopologyTrialStatusText.Text = apply.Error ?? "Apply failed.";
                return;
            }

            topologyTrialSnapshot = apply.BeforeSnapshot;
            RefreshDisplaysInternal();
            StartTopologyTrialCountdown(8);
            SetTopologyTrialCancelEnabled(true);
            TopologyTrialStatusText.Text = string.Format(
                TryFindResource("LOCDisplayManager_TopologyTrialAppliedFormat") as string
                ?? "Applied ({0}). Restoring in {1}s…",
                apply.Message,
                topologyTrialSecondsLeft);
        }

        private void TopologyTrialRestore_OnClick(object sender, RoutedEventArgs e)
        {
            RestoreTopologyTrial(manual: true);
        }

        private void StartTopologyTrialCountdown(int seconds)
        {
            StopTopologyTrialTimer(restore: false);
            topologyTrialSecondsLeft = seconds;
            topologyTrialTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            topologyTrialTimer.Tick += TopologyTrialTimer_OnTick;
            topologyTrialTimer.Start();
            SetTopologyTrialCancelEnabled(true);
        }

        private void TopologyTrialTimer_OnTick(object sender, EventArgs e)
        {
            topologyTrialSecondsLeft--;
            if (topologyTrialSecondsLeft > 0)
            {
                TopologyTrialStatusText.Text = string.Format(
                    TryFindResource("LOCDisplayManager_TopologyTrialCountdownFormat") as string
                    ?? "Restoring in {0}s…",
                    topologyTrialSecondsLeft);
                return;
            }

            RestoreTopologyTrial(manual: false);
        }

        private void StopTopologyTrialTimer(bool restore)
        {
            if (topologyTrialTimer != null)
            {
                topologyTrialTimer.Stop();
                topologyTrialTimer.Tick -= TopologyTrialTimer_OnTick;
                topologyTrialTimer = null;
            }

            if (restore)
            {
                RestoreTopologyTrial(manual: false);
            }
        }

        private void SetTopologyTrialCancelEnabled(bool enabled)
        {
            if (TopologyTrialCancelButton != null)
            {
                TopologyTrialCancelButton.IsEnabled = enabled;
            }
        }

        private void RestoreTopologyTrial(bool manual)
        {
            if (topologyTrialTimer != null)
            {
                topologyTrialTimer.Stop();
                topologyTrialTimer.Tick -= TopologyTrialTimer_OnTick;
                topologyTrialTimer = null;
            }

            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            var snapshot = topologyTrialSnapshot;
            topologyTrialSnapshot = null;
            SetTopologyTrialCancelEnabled(false);
            if (plugin == null || snapshot == null)
            {
                plugin?.DisarmRestoreLease();
                if (manual)
                {
                    TopologyTrialStatusText.Text = TryFindResource("LOCDisplayManager_TopologyTrialNothingToRestore") as string
                        ?? "No trial snapshot to restore.";
                }

                return;
            }

            if (plugin.TryRestoreLastLeaseSnapshot(snapshot, out var error))
            {
                RefreshDisplaysInternal();
                TopologyTrialStatusText.Text = TryFindResource("LOCDisplayManager_TopologyTrialRestored") as string
                    ?? "Desktop topology restored.";
            }
            else
            {
                TopologyTrialStatusText.Text = error ?? "Restore failed.";
            }
        }

        private void HdrPolicyRadio_OnChecked(object sender, RoutedEventArgs e)
        {
            if (syncingHdrPolicy)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            if (HdrPolicyAllRadio?.IsChecked == true)
            {
                settings.SetHdrPolicyForMode(editingLaunchMode, GlobalHdrPolicy.OnForAllGames);
            }
            else if (HdrPolicyMetadataRadio?.IsChecked == true)
            {
                settings.SetHdrPolicyForMode(editingLaunchMode, GlobalHdrPolicy.OnWhenMetadataIndicates);
            }
            else if (HdrPolicyPlayniteNativeRadio?.IsChecked == true)
            {
                settings.SetHdrPolicyForMode(editingLaunchMode, GlobalHdrPolicy.UsePlayniteNative);
            }
            else
            {
                settings.SetHdrPolicyForMode(editingLaunchMode, GlobalHdrPolicy.DoNotManage);
            }

            SyncHdrMetadataPanelVisibility();
            UpdateOverview();
        }

        private void SyncHdrPolicyRadios()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || HdrPolicyNoneRadio == null)
            {
                return;
            }

            syncingHdrPolicy = true;
            try
            {
                switch (settings.GetHdrPolicyForMode(editingLaunchMode))
                {
                    case GlobalHdrPolicy.OnForAllGames:
                        HdrPolicyAllRadio.IsChecked = true;
                        break;
                    case GlobalHdrPolicy.OnWhenMetadataIndicates:
                        HdrPolicyMetadataRadio.IsChecked = true;
                        break;
                    case GlobalHdrPolicy.UsePlayniteNative:
                        HdrPolicyPlayniteNativeRadio.IsChecked = true;
                        break;
                    default:
                        HdrPolicyNoneRadio.IsChecked = true;
                        break;
                }
            }
            finally
            {
                syncingHdrPolicy = false;
            }

            SyncHdrMetadataPanelVisibility();
        }

        private void SyncHdrMetadataPanelVisibility()
        {
            if (HdrMetadataPanel == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            var show = settings?.GetHdrPolicyForMode(editingLaunchMode) == GlobalHdrPolicy.OnWhenMetadataIndicates
                || HdrPolicyMetadataRadio?.IsChecked == true;
            HdrMetadataPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SyncHdrMetadataControls()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            syncingHdrMetadata = true;
            try
            {
                if (HdrMetadataNamesBox != null)
                {
                    HdrMetadataNamesBox.Text = settings.HdrMetadataMatchNamesText;
                }

                if (HdrIncludeTagsCheck != null)
                {
                    HdrIncludeTagsCheck.IsChecked = settings.IncludeTagsInHdrMetadataMatch;
                }
            }
            finally
            {
                syncingHdrMetadata = false;
            }

            SyncHdrMetadataPanelVisibility();
        }

        private void HdrMetadataNamesBox_OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (syncingHdrMetadata)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || HdrMetadataNamesBox == null)
            {
                return;
            }

            settings.HdrMetadataMatchNamesText = HdrMetadataNamesBox.Text;
            HdrMetadataNamesBox.Text = settings.HdrMetadataMatchNamesText;
            UpdateOverview();
        }

        private void HdrIncludeTagsCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            if (syncingHdrMetadata)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || HdrIncludeTagsCheck == null)
            {
                return;
            }

            settings.IncludeTagsInHdrMetadataMatch = HdrIncludeTagsCheck.IsChecked == true;
            UpdateOverview();
        }

        private void HdrWriteOffNow_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (plugin == null)
            {
                return;
            }

            if (plugin.TryWriteHdrOffNow(out var error))
            {
                HdrStatusText.Text = TryFindResource("LOCDisplayManager_HdrWriteOffOk") as string
                    ?? "HDR off was written to capable displays (no readback trust).";
            }
            else
            {
                HdrStatusText.Text = error ?? "HDR write failed.";
            }

            UpdateOverview();
        }

        private void UpdateOverview()
        {
            var settings = DataContext as DisplayManagerSettings;
            var displays = settings?.AvailableDisplays;
            var connected = displays?.Where(d => d.IsConnected).ToList()
                ?? new System.Collections.Generic.List<DisplayInfo>();
            var desktopPlay = ResolvePreferredPlayDisplayForMode(settings, connected, ApplicationMode.Desktop);
            var fullscreenPlay = ResolvePreferredPlayDisplayForMode(settings, connected, ApplicationMode.Fullscreen);
            var playDisplay = ResolvePreferredPlayDisplay(settings, connected);

            if (OverviewDisplaysText != null)
            {
                if (connected.Count == 0)
                {
                    OverviewDisplaysText.Text = TryFindResource("LOCDisplayManager_OverviewDisplaysNone") as string
                        ?? "No displays detected.";
                }
                else
                {
                    var format = TryFindResource("LOCDisplayManager_OverviewDisplaysConnectedFormat") as string
                        ?? "{0} connected display(s)";
                    OverviewDisplaysText.Text = string.Format(format, connected.Count);
                }
            }

            if (OverviewDisplayPills != null)
            {
                OverviewDisplayPills.Children.Clear();
                AddOverviewPlayPrimaryPills(
                    OverviewDisplayPills,
                    settings,
                    connected,
                    ApplicationMode.Desktop,
                    desktopPlay);
                AddOverviewPlayPrimaryPills(
                    OverviewDisplayPills,
                    settings,
                    connected,
                    ApplicationMode.Fullscreen,
                    fullscreenPlay);
            }

            var playHdrSupported = ProbeDisplayHdrSupported(settings, playDisplay);
            if (OverviewPolicyText != null)
            {
                if (!playHdrSupported)
                {
                    OverviewPolicyText.Text = TryFindResource("LOCDisplayManager_HdrPrimaryNoSupport") as string
                        ?? "The selected primary display does not support HDR.";
                }
                else
                {
                    OverviewPolicyText.Text = settings?.Plugin?.GetHdrActionOverviewText()
                        ?? settings?.Plugin?.GetHdrPolicyOverviewText()
                        ?? (TryFindResource("LOCDisplayManager_OverviewPolicyUnset") as string ?? "Not configured yet.");
                }
            }

            if (OverviewHdrNoSupportText != null)
            {
                OverviewHdrNoSupportText.Visibility = Visibility.Collapsed;
            }

            if (OverviewPolicyPills != null)
            {
                OverviewPolicyPills.Children.Clear();
                if (playHdrSupported)
                {
                    var hdrValue = GetHdrPolicyBadgeValue(settings);
                    var managingHdr = settings?.GlobalHdrPolicy == GlobalHdrPolicy.OnForAllGames
                        || settings?.GlobalHdrPolicy == GlobalHdrPolicy.OnWhenMetadataIndicates;
                    OverviewPolicyPills.Children.Add(CreateStatusBadge(
                        null,
                        hdrValue,
                        managingHdr ? "PositiveRatingBrush" : "GlyphBrush",
                        managingHdr ? 1.0 : 0.9));
                }
            }

            var nativeEnabled = GetCachedNativeHdrEnabledCount(settings?.Plugin);
            if (OverviewNativeHdrText != null)
            {
                if (nativeEnabled <= 0)
                {
                    OverviewNativeHdrText.Text =
                        TryFindResource("LOCDisplayManager_OverviewNativeHdrClear") as string
                        ?? "No games have Playnite's Enable HDR option turned on.";
                }
                else
                {
                    var conflictFormat =
                        TryFindResource("LOCDisplayManager_OverviewNativeHdrConflictFormat") as string
                        ?? "{0} game(s) still have Playnite Enable HDR turned on.";
                    OverviewNativeHdrText.Text = string.Format(conflictFormat, nativeEnabled);
                }
            }

            if (OverviewNativeHdrStatusText != null)
            {
                var nativeValue = nativeEnabled > 0
                    ? (TryFindResource("LOCDisplayManager_StatusEnabled") as string ?? "Enabled")
                    : (TryFindResource("LOCDisplayManager_StatusDisabled") as string ?? "Disabled");
                OverviewNativeHdrStatusText.Text = nativeValue;
                ApplyStatusBadgeAppearance(
                    OverviewNativeHdrStatusText,
                    nativeEnabled > 0 ? "WarningBrush" : "PositiveRatingBrush");
            }

            if (OverviewRefreshRateText != null)
            {
                OverviewRefreshRateText.Text = settings?.Plugin?.GetRefreshRateOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_RefreshPolicyNative") as string
                        ?? "Native (do not change refresh rate)");
            }
        }

        private DisplayInfo ResolvePreferredPlayDisplay(
            DisplayManagerSettings settings,
            System.Collections.Generic.IList<DisplayInfo> connected)
        {
            return ResolvePreferredPlayDisplayForMode(settings, connected, editingLaunchMode);
        }

        private static DisplayInfo ResolvePreferredPlayDisplayForMode(
            DisplayManagerSettings settings,
            System.Collections.Generic.IList<DisplayInfo> connected,
            ApplicationMode mode)
        {
            if (connected == null || connected.Count == 0)
            {
                return null;
            }

            var preferredId = settings?.GetDefaultDisplayProfileForMode(mode)?.PreferredPlayDisplayId;
            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                var match = connected.FirstOrDefault(d =>
                    string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return connected.FirstOrDefault(d => d.IsPrimary) ?? connected[0];
        }

        private void AddOverviewPlayPrimaryPills(
            WrapPanel pills,
            DisplayManagerSettings settings,
            IList<DisplayInfo> connected,
            ApplicationMode mode,
            DisplayInfo playDisplay)
        {
            if (pills == null || connected == null || connected.Count == 0)
            {
                return;
            }

            var modeLabel = mode == ApplicationMode.Fullscreen
                ? (TryFindResource("LOCDisplayManager_LaunchModeFullscreen") as string ?? "Fullscreen")
                : (TryFindResource("LOCDisplayManager_LaunchModeDesktop") as string ?? "Desktop");

            var preferredId = settings?.GetDefaultDisplayProfileForMode(mode)?.PreferredPlayDisplayId;
            var name = !string.IsNullOrWhiteSpace(preferredId)
                ? (playDisplay?.EffectiveName
                    ?? (TryFindResource("LOCDisplayManager_StatusUnknown") as string ?? "Unknown"))
                : (TryFindResource("LOCDisplayManager_PlayDisplayWindowsDefault") as string
                    ?? "Keep Windows default");

            var format = TryFindResource("LOCDisplayManager_OverviewPlayPrimaryFormat") as string
                ?? "{0}: {1}";
            pills.Children.Add(CreateStatusBadge(
                null,
                string.Format(format, modeLabel, name),
                "PositiveRatingBrush"));
        }

        private bool ProbeDisplayHdrSupported(DisplayManagerSettings settings, DisplayInfo display)
        {
            if (display == null || settings?.Plugin?.Hdr == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(display.Id)
                && hdrSupportCache.TryGetValue(display.Id, out var cached))
            {
                return cached;
            }

            bool supported;
            try
            {
                var probe = settings.Plugin.Hdr.ProbeActiveTargets(new[] { display }).FirstOrDefault();
                supported = probe != null && probe.HdrSupported;
            }
            catch
            {
                supported = false;
            }

            if (!string.IsNullOrWhiteSpace(display.Id))
            {
                hdrSupportCache[display.Id] = supported;
            }

            return supported;
        }

        private void SyncHdrCapabilityUi()
        {
            var settings = DataContext as DisplayManagerSettings;
            var connected = settings?.AvailableDisplays?.Where(d => d.IsConnected).ToList()
                ?? new System.Collections.Generic.List<DisplayInfo>();
            var playDisplay = ResolvePreferredPlayDisplay(settings, connected);
            var supported = ProbeDisplayHdrSupported(settings, playDisplay);

            if (HdrPrimaryNoSupportCallout != null)
            {
                HdrPrimaryNoSupportCallout.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
            }

            if (HdrOptionsPanel != null)
            {
                HdrOptionsPanel.IsEnabled = supported;
                HdrOptionsPanel.Opacity = supported ? 1.0 : 0.55;
            }
        }

        private string GetHdrPolicyBadgeValue(DisplayManagerSettings settings)
        {
            switch (settings?.GetHdrPolicyForMode(editingLaunchMode) ?? GlobalHdrPolicy.DoNotManage)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    return TryFindResource("LOCDisplayManager_OverviewActionAlwaysOnShort") as string
                        ?? "Always on";
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    return TryFindResource("LOCDisplayManager_OverviewActionMetadataShort") as string
                        ?? "Metadata";
                case GlobalHdrPolicy.UsePlayniteNative:
                    return TryFindResource("LOCDisplayManager_OverviewActionPlayniteNativeShort") as string
                        ?? "Playnite native";
                default:
                    return TryFindResource("LOCDisplayManager_OverviewActionDoNotManageShort") as string
                        ?? "Do not manage";
            }
        }

        private void SyncDesktopAccessControls()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || ShowDesktopTopPanelCheck == null)
            {
                return;
            }

            ShowDesktopTopPanelCheck.IsChecked = settings.ShowDesktopTopPanel;
            // DesktopTopPanelDisplayMode ComboBox uses TwoWay binding to DesktopTopPanelDisplayModeValue.
        }

        private void ShowDesktopTopPanelCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || ShowDesktopTopPanelCheck == null)
            {
                return;
            }

            settings.ShowDesktopTopPanel = ShowDesktopTopPanelCheck.IsChecked == true;
            settings.Plugin?.NotifyDisplaysChanged();
            settings.Plugin?.Theme?.Refresh();
        }

        private void ExpanderChevronButton_OnClick(object sender, RoutedEventArgs e)
        {
            for (var parent = VisualTreeHelper.GetParent(sender as DependencyObject);
                 parent != null;
                 parent = VisualTreeHelper.GetParent(parent))
            {
                var expander = parent as Expander;
                if (expander == null)
                {
                    continue;
                }

                expander.IsExpanded = !expander.IsExpanded;
                e.Handled = true;
                return;
            }
        }

        private void SyncRefreshRateRadios()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || RefreshPolicyNativeRadio == null)
            {
                return;
            }

            var connected = settings.AvailableDisplays?
                .Where(d => d.IsConnected)
                .ToList() ?? new System.Collections.Generic.List<DisplayInfo>();
            var primary = ResolvePreferredPlayDisplay(settings, connected);

            BuildRefreshRatePrimaryPanel(settings, primary);

            syncingRefreshRadios = true;
            try
            {
                var rates = GetCachedAvailableRates(settings, primary);
                var preferred = settings.GetPreferredRefreshRateHzForMode(editingLaunchMode);
                var items = BuildRefreshExactItems(rates, preferred);
                if (RefreshExactBox != null)
                {
                    RefreshExactBox.ItemsSource = items;
                    RefreshExactItem selected = null;
                    if (preferred.HasValue)
                    {
                        selected = items.FirstOrDefault(i =>
                            Math.Abs(i.Hz - preferred.Value) < 0.05);
                    }

                    if (selected == null
                        && (settings.GetRefreshRatePolicyForMode(editingLaunchMode) == RefreshRatePolicy.ExactHz
                            || settings.GetRefreshRatePolicyForMode(editingLaunchMode) == RefreshRatePolicy.Prefer60
                            || settings.GetRefreshRatePolicyForMode(editingLaunchMode) == RefreshRatePolicy.Prefer120))
                    {
                        selected = items.FirstOrDefault(i => !i.IsUnavailable);
                    }

                    RefreshExactBox.SelectedItem = selected;
                    UpdateRefreshUnavailableHint(selected);
                }

                switch (settings.GetRefreshRatePolicyForMode(editingLaunchMode))
                {
                    case RefreshRatePolicy.HighestDetected:
                        RefreshPolicyHighestRadio.IsChecked = true;
                        break;
                    case RefreshRatePolicy.ExactHz:
                    case RefreshRatePolicy.Prefer60:
                    case RefreshRatePolicy.Prefer120:
                        if (RefreshPolicyExactRadio != null)
                        {
                            RefreshPolicyExactRadio.IsChecked = true;
                        }
                        else
                        {
                            RefreshPolicyNativeRadio.IsChecked = true;
                        }
                        break;
                    default:
                        RefreshPolicyNativeRadio.IsChecked = true;
                        break;
                }

                if (RefreshExactBox != null)
                {
                    RefreshExactBox.IsEnabled =
                        RefreshPolicyExactRadio?.IsChecked == true && items.Count > 0;
                }

                if (RefreshExactRefreshButton != null)
                {
                    RefreshExactRefreshButton.IsEnabled = true;
                }
            }
            finally
            {
                syncingRefreshRadios = false;
            }
        }

        private List<RefreshExactItem> BuildRefreshExactItems(
            IReadOnlyList<double> rates,
            double? preferredHz)
        {
            var format = TryFindResource("LOCDisplayManager_RefreshPolicyExactFormat") as string
                ?? "{0:0.###} Hz";
            var items = (rates ?? Array.Empty<double>())
                .Select(rate => new RefreshExactItem
                {
                    Hz = rate,
                    Label = string.Format(CultureInfo.CurrentCulture, format, rate)
                })
                .ToList();

            if (preferredHz.HasValue
                && !items.Any(i => Math.Abs(i.Hz - preferredHz.Value) < 0.05))
            {
                items.Insert(0, new RefreshExactItem
                {
                    Hz = preferredHz.Value,
                    IsUnavailable = true,
                    Label = string.Format(CultureInfo.CurrentCulture, format, preferredHz.Value)
                        + " · "
                        + (TryFindResource("LOCDisplayManager_StatusUnknown") as string ?? "Unavailable")
                });
            }

            return items;
        }

        private void UpdateRefreshUnavailableHint(RefreshExactItem selected)
        {
            if (RefreshExactMissingHint == null)
            {
                return;
            }

            RefreshExactMissingHint.Visibility =
                selected != null && selected.IsUnavailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void RefreshExactRefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            settings?.Plugin?.RefreshRates?.ClearCache();
            cachedRatesDisplayId = null;
            cachedRates = null;
            SyncRefreshRateRadios();
        }

        private void BuildRefreshRatePrimaryPanel(DisplayManagerSettings settings, DisplayInfo primary)
        {
            if (RefreshRatePrimaryPanel == null)
            {
                return;
            }

            RefreshRatePrimaryPanel.Children.Clear();
            if (primary == null)
            {
                RefreshRatePrimaryPanel.Children.Add(new TextBlock
                {
                    Text = TryFindResource("LOCDisplayManager_RefreshRateDetectedNone") as string
                        ?? "No primary display detected.",
                    Style = TryFindResource("HintText") as Style
                });
                return;
            }

            RefreshRatePrimaryPanel.Children.Add(new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_RefreshRatePrimaryLabel") as string
                    ?? "Primary display for games",
                Style = TryFindResource("FieldLabel") as Style
            });
            RefreshRatePrimaryPanel.Children.Add(new TextBlock
            {
                Text = primary.EffectiveName,
                Style = TryFindResource("SummaryCardTitle") as Style,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (primary.Width > 0 && primary.Height > 0)
            {
                pills.Children.Add(CreateStatusBadge(
                    TryFindResource("LOCDisplayManager_RefreshRateResolutionLabel") as string ?? "Resolution",
                    $"{primary.Width}×{primary.Height}",
                    "GlyphBrush",
                    0.95));
            }

            if (primary.RefreshRateHz > 0)
            {
                pills.Children.Add(CreateStatusBadge(
                    TryFindResource("LOCDisplayManager_RefreshRateCurrentLabel") as string ?? "Current refresh rate",
                    primary.RefreshRateHz.ToString("0.###") + " Hz",
                    "PositiveRatingBrush"));
            }

            RefreshRatePrimaryPanel.Children.Add(pills);

            var rates = GetCachedAvailableRates(settings, primary);
            RefreshRatePrimaryPanel.Children.Add(new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_RefreshRateAvailableLabel") as string
                    ?? "Available at this resolution",
                Style = TryFindResource("FieldLabel") as Style,
                Margin = new Thickness(0, 4, 0, 4)
            });

            if (rates.Count == 0)
            {
                RefreshRatePrimaryPanel.Children.Add(new TextBlock
                {
                    Text = "—",
                    Style = TryFindResource("HintText") as Style
                });
                return;
            }

            var ratePills = new WrapPanel();
            foreach (var rate in rates)
            {
                ratePills.Children.Add(CreateStatusBadge(
                    null,
                    rate.ToString("0.###") + " Hz",
                    "GlyphBrush",
                    0.95));
            }

            RefreshRatePrimaryPanel.Children.Add(ratePills);
        }

        private static bool IsConfiguredPlayPrimaryForMode(
            DisplayInfo display,
            DisplayManagerSettings settings,
            ApplicationMode mode)
        {
            if (display == null || settings == null)
            {
                return false;
            }

            var preferredId = settings.GetDefaultDisplayProfileForMode(mode)?.PreferredPlayDisplayId;
            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                return string.Equals(display.Id, preferredId, StringComparison.OrdinalIgnoreCase);
            }

            // Keep Windows default → badge the current Windows primary.
            return display.IsPrimary;
        }

        private void RefreshRateRadio_OnChecked(object sender, RoutedEventArgs e)
        {
            if (syncingRefreshRadios)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            var radio = sender as RadioButton;
            if (radio == null || radio.IsChecked != true)
            {
                return;
            }

            if (ReferenceEquals(radio, RefreshPolicyHighestRadio)
                || string.Equals(radio.Tag as string, "HighestDetected", StringComparison.OrdinalIgnoreCase))
            {
                settings.SetRefreshRatePolicyForMode(editingLaunchMode, RefreshRatePolicy.HighestDetected);
                settings.SetPreferredRefreshRateHzForMode(editingLaunchMode, null);
            }
            else if (ReferenceEquals(radio, RefreshPolicyExactRadio)
                || string.Equals(radio.Tag as string, "ExactHz", StringComparison.OrdinalIgnoreCase))
            {
                settings.SetRefreshRatePolicyForMode(editingLaunchMode, RefreshRatePolicy.ExactHz);
                ApplySelectedExactRefreshRate(settings);
            }
            else
            {
                settings.SetRefreshRatePolicyForMode(editingLaunchMode, RefreshRatePolicy.Native);
                settings.SetPreferredRefreshRateHzForMode(editingLaunchMode, null);
            }

            if (RefreshExactBox != null)
            {
                var count = (RefreshExactBox.ItemsSource as IEnumerable<RefreshExactItem>)?.Count()
                    ?? RefreshExactBox.Items.Count;
                RefreshExactBox.IsEnabled =
                    settings.GetRefreshRatePolicyForMode(editingLaunchMode) == RefreshRatePolicy.ExactHz && count > 0;
            }

            if (OverviewRefreshRateText != null)
            {
                OverviewRefreshRateText.Text = settings.Plugin?.GetRefreshRateOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_RefreshPolicyNative") as string
                        ?? "Native (do not change refresh rate)");
            }
        }

        private void RefreshExactBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingRefreshRadios)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            if (RefreshPolicyExactRadio?.IsChecked != true
                && settings.GetRefreshRatePolicyForMode(editingLaunchMode) != RefreshRatePolicy.ExactHz
                && settings.GetRefreshRatePolicyForMode(editingLaunchMode) != RefreshRatePolicy.Prefer60
                && settings.GetRefreshRatePolicyForMode(editingLaunchMode) != RefreshRatePolicy.Prefer120)
            {
                return;
            }

            settings.SetRefreshRatePolicyForMode(editingLaunchMode, RefreshRatePolicy.ExactHz);
            if (RefreshPolicyExactRadio != null && RefreshPolicyExactRadio.IsChecked != true)
            {
                syncingRefreshRadios = true;
                try
                {
                    RefreshPolicyExactRadio.IsChecked = true;
                }
                finally
                {
                    syncingRefreshRadios = false;
                }
            }

            ApplySelectedExactRefreshRate(settings);
            if (RefreshExactBox != null)
            {
                RefreshExactBox.IsEnabled = RefreshExactBox.Items.Count > 0;
            }

            if (OverviewRefreshRateText != null)
            {
                OverviewRefreshRateText.Text = settings.Plugin?.GetRefreshRateOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_RefreshPolicyNative") as string
                        ?? "Native (do not change refresh rate)");
            }
        }

        private void ApplySelectedExactRefreshRate(DisplayManagerSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            var item = RefreshExactBox?.SelectedItem as RefreshExactItem;
            if (item == null && RefreshExactBox?.Items.Count > 0)
            {
                syncingRefreshRadios = true;
                try
                {
                    var first = (RefreshExactBox.ItemsSource as IEnumerable<RefreshExactItem>)
                        ?.FirstOrDefault(i => !i.IsUnavailable)
                        ?? (RefreshExactBox.ItemsSource as IEnumerable<RefreshExactItem>)
                        ?.FirstOrDefault();
                    if (first != null)
                    {
                        RefreshExactBox.SelectedItem = first;
                        item = first;
                    }
                }
                finally
                {
                    syncingRefreshRadios = false;
                }
            }

            if (item == null)
            {
                UpdateRefreshUnavailableHint(null);
                settings.SetPreferredRefreshRateHzForMode(editingLaunchMode, null);
                return;
            }

            settings.SetPreferredRefreshRateHzForMode(editingLaunchMode, item.Hz);
            UpdateRefreshUnavailableHint(item);
        }

        private void SyncResolutionRadios()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || ResolutionPolicyNativeRadio == null)
            {
                return;
            }

            var connected = settings.AvailableDisplays?
                .Where(d => d.IsConnected)
                .ToList() ?? new List<DisplayInfo>();
            var primary = ResolvePreferredPlayDisplay(settings, connected);

            BuildResolutionPrimaryPanel(settings, primary);

            syncingResolutionRadios = true;
            try
            {
                var modes = PrepareResolutionModesForUi(GetCachedAvailableModes(settings, primary));
                if (ResolutionExactBox != null)
                {
                    settings.GetPreferredResolutionForMode(editingLaunchMode, out var preferredW, out var preferredH);
                    var items = BuildResolutionExactItems(modes, preferredW, preferredH);
                    ResolutionExactBox.ItemsSource = items;

                    ResolutionExactItem selected = null;
                    if (preferredW > 0 && preferredH > 0)
                    {
                        selected = items.FirstOrDefault(i =>
                            !i.IsHeader
                            && i.Width == preferredW.Value
                            && i.Height == preferredH.Value);
                    }

                    if (selected == null
                        && settings.GetResolutionPolicyForMode(editingLaunchMode) == ResolutionPolicy.Exact)
                    {
                        selected = items.FirstOrDefault(i => !i.IsHeader && !i.IsUnavailable);
                    }

                    ResolutionExactBox.SelectedItem = selected;
                    UpdateResolutionUnavailableHint(selected);
                }

                switch (settings.GetResolutionPolicyForMode(editingLaunchMode))
                {
                    case ResolutionPolicy.LowestAvailable:
                        if (ResolutionPolicyLowestRadio != null)
                        {
                            ResolutionPolicyLowestRadio.IsChecked = true;
                        }
                        break;
                    case ResolutionPolicy.HighestAvailable:
                        if (ResolutionPolicyHighestRadio != null)
                        {
                            ResolutionPolicyHighestRadio.IsChecked = true;
                        }
                        break;
                    case ResolutionPolicy.Exact:
                        if (ResolutionPolicyExactRadio != null)
                        {
                            ResolutionPolicyExactRadio.IsChecked = true;
                        }
                        else
                        {
                            ResolutionPolicyNativeRadio.IsChecked = true;
                        }
                        break;
                    default:
                        ResolutionPolicyNativeRadio.IsChecked = true;
                        break;
                }

                if (ResolutionExactBox != null)
                {
                    var selectableCount = (ResolutionExactBox.ItemsSource as IEnumerable<ResolutionExactItem>)
                        ?.Count(i => !i.IsHeader) ?? ResolutionExactBox.Items.Count;
                    ResolutionExactBox.IsEnabled =
                        ResolutionPolicyExactRadio?.IsChecked == true && selectableCount > 0;
                }

                if (ResolutionExactRefreshButton != null)
                {
                    ResolutionExactRefreshButton.IsEnabled = true;
                }
            }
            finally
            {
                syncingResolutionRadios = false;
            }
        }

        private List<ResolutionMode> PrepareResolutionModesForUi(IReadOnlyList<ResolutionMode> source)
        {
            var monitorGroup = TryFindResource("LOCDisplayManager_ResolutionGroupMonitor") as string
                ?? "From monitor";
            var customGroup = TryFindResource("LOCDisplayManager_ResolutionGroupCustom") as string
                ?? "Additional";
            var list = (source ?? Array.Empty<ResolutionMode>()).ToList();
            foreach (var mode in list)
            {
                mode.GroupName = mode.IsCustom ? customGroup : monitorGroup;
            }

            return list;
        }

        private List<ResolutionExactItem> BuildResolutionExactItems(
            IReadOnlyList<ResolutionMode> modes,
            int? preferredWidth,
            int? preferredHeight)
        {
            var list = modes?.ToList() ?? new List<ResolutionMode>();
            var items = new List<ResolutionExactItem>();
            var monitorGroup = TryFindResource("LOCDisplayManager_ResolutionGroupMonitor") as string
                ?? "From monitor";
            var customGroup = TryFindResource("LOCDisplayManager_ResolutionGroupCustom") as string
                ?? "Additional";
            var unavailableFormat = TryFindResource("LOCDisplayManager_ResolutionUnavailableFormat") as string
                ?? "{0} (unavailable)";

            var monitorModes = list.Where(m => !m.IsCustom).ToList();
            var customModes = list.Where(m => m.IsCustom).ToList();
            var useGroups = monitorModes.Count > 0 && customModes.Count > 0;

            void AddModes(IEnumerable<ResolutionMode> groupModes)
            {
                foreach (var mode in groupModes)
                {
                    items.Add(new ResolutionExactItem
                    {
                        Mode = mode,
                        Label = mode.Label
                    });
                }
            }

            if (useGroups)
            {
                items.Add(new ResolutionExactItem { IsHeader = true, Label = monitorGroup });
                AddModes(monitorModes);
                items.Add(new ResolutionExactItem { IsHeader = true, Label = customGroup });
                AddModes(customModes);
            }
            else
            {
                AddModes(list);
            }

            if (preferredWidth > 0 && preferredHeight > 0
                && !list.Any(m => m.Width == preferredWidth.Value && m.Height == preferredHeight.Value))
            {
                var missing = new ResolutionMode
                {
                    Width = preferredWidth.Value,
                    Height = preferredHeight.Value,
                    IsCustom = true,
                    GroupName = customGroup
                };
                var missingItem = new ResolutionExactItem
                {
                    Mode = missing,
                    IsUnavailable = true,
                    Label = string.Format(unavailableFormat, missing.Label)
                };

                if (useGroups)
                {
                    var customHeaderIndex = items.FindIndex(i => i.IsHeader && i.Label == customGroup);
                    if (customHeaderIndex >= 0)
                    {
                        items.Insert(customHeaderIndex + 1, missingItem);
                    }
                    else
                    {
                        items.Add(new ResolutionExactItem { IsHeader = true, Label = customGroup });
                        items.Add(missingItem);
                    }
                }
                else
                {
                    items.Insert(0, missingItem);
                }
            }

            return items;
        }

        private void UpdateResolutionUnavailableHint(ResolutionExactItem selected)
        {
            if (ResolutionExactMissingHint == null)
            {
                return;
            }

            ResolutionExactMissingHint.Visibility =
                selected != null && selected.IsUnavailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ResolutionExactRefreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            settings?.Plugin?.Resolutions?.ClearCache();
            cachedModesDisplayId = null;
            cachedModes = null;
            SyncResolutionRadios();
        }

        private void BuildResolutionPrimaryPanel(DisplayManagerSettings settings, DisplayInfo primary)
        {
            if (ResolutionPrimaryPanel == null)
            {
                return;
            }

            ResolutionPrimaryPanel.Children.Clear();
            if (primary == null)
            {
                ResolutionPrimaryPanel.Children.Add(new TextBlock
                {
                    Text = TryFindResource("LOCDisplayManager_RefreshRateDetectedNone") as string
                        ?? "No primary display detected.",
                    Style = TryFindResource("HintText") as Style
                });
                return;
            }

            ResolutionPrimaryPanel.Children.Add(new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_RefreshRatePrimaryLabel") as string
                    ?? "Primary display for games",
                Style = TryFindResource("FieldLabel") as Style
            });
            ResolutionPrimaryPanel.Children.Add(new TextBlock
            {
                Text = primary.EffectiveName,
                Style = TryFindResource("SummaryCardTitle") as Style,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            if (primary.Width > 0 && primary.Height > 0)
            {
                var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                pills.Children.Add(CreateStatusBadge(
                    TryFindResource("LOCDisplayManager_ResolutionCurrentLabel") as string ?? "Current resolution",
                    $"{primary.Width}×{primary.Height}",
                    "PositiveRatingBrush"));
                ResolutionPrimaryPanel.Children.Add(pills);
            }

            var modeCount = GetCachedAvailableModes(settings, primary).Count;
            ResolutionPrimaryPanel.Children.Add(new TextBlock
            {
                Text = modeCount > 0
                    ? string.Format(
                        TryFindResource("LOCDisplayManager_ResolutionAvailableCountFormat") as string
                        ?? "{0} modes available in the list below",
                        modeCount)
                    : (TryFindResource("LOCDisplayManager_ResolutionAvailableLabel") as string ?? "Available modes"),
                Style = TryFindResource("HintText") as Style,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        private void ResolutionRadio_OnChecked(object sender, RoutedEventArgs e)
        {
            if (syncingResolutionRadios)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            var radio = sender as RadioButton;
            if (radio == null || radio.IsChecked != true)
            {
                return;
            }

            if (ReferenceEquals(radio, ResolutionPolicyLowestRadio)
                || string.Equals(radio.Tag as string, "LowestAvailable", StringComparison.OrdinalIgnoreCase))
            {
                settings.SetResolutionPolicyForMode(editingLaunchMode, ResolutionPolicy.LowestAvailable);
                settings.SetPreferredResolutionForMode(editingLaunchMode, null, null);
            }
            else if (ReferenceEquals(radio, ResolutionPolicyHighestRadio)
                || string.Equals(radio.Tag as string, "HighestAvailable", StringComparison.OrdinalIgnoreCase))
            {
                settings.SetResolutionPolicyForMode(editingLaunchMode, ResolutionPolicy.HighestAvailable);
                settings.SetPreferredResolutionForMode(editingLaunchMode, null, null);
            }
            else if (ReferenceEquals(radio, ResolutionPolicyExactRadio)
                || string.Equals(radio.Tag as string, "Exact", StringComparison.OrdinalIgnoreCase))
            {
                settings.SetResolutionPolicyForMode(editingLaunchMode, ResolutionPolicy.Exact);
                ApplySelectedExactResolution(settings);
            }
            else
            {
                settings.SetResolutionPolicyForMode(editingLaunchMode, ResolutionPolicy.Native);
                settings.SetPreferredResolutionForMode(editingLaunchMode, null, null);
            }

            if (ResolutionExactBox != null)
            {
                ResolutionExactBox.IsEnabled = settings.GetResolutionPolicyForMode(editingLaunchMode) == ResolutionPolicy.Exact
                    && ResolutionExactBox.Items.Count > 0;
            }
        }

        private void ResolutionExactBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingResolutionRadios)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            if (ResolutionPolicyExactRadio?.IsChecked != true
                && settings.GetResolutionPolicyForMode(editingLaunchMode) != ResolutionPolicy.Exact)
            {
                return;
            }

            settings.SetResolutionPolicyForMode(editingLaunchMode, ResolutionPolicy.Exact);
            if (ResolutionPolicyExactRadio != null && ResolutionPolicyExactRadio.IsChecked != true)
            {
                syncingResolutionRadios = true;
                try
                {
                    ResolutionPolicyExactRadio.IsChecked = true;
                }
                finally
                {
                    syncingResolutionRadios = false;
                }
            }

            ApplySelectedExactResolution(settings);
            if (ResolutionExactBox != null)
            {
                ResolutionExactBox.IsEnabled = ResolutionExactBox.Items.Count > 0;
            }
        }

        private void ApplySelectedExactResolution(DisplayManagerSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            var item = ResolutionExactBox?.SelectedItem as ResolutionExactItem;
            if (item != null && item.IsHeader)
            {
                UpdateResolutionUnavailableHint(null);
                return;
            }

            if (item == null && ResolutionExactBox?.Items.Count > 0)
            {
                syncingResolutionRadios = true;
                try
                {
                    var first = (ResolutionExactBox.ItemsSource as IEnumerable<ResolutionExactItem>)
                        ?.FirstOrDefault(i => !i.IsHeader && !i.IsUnavailable)
                        ?? (ResolutionExactBox.ItemsSource as IEnumerable<ResolutionExactItem>)
                        ?.FirstOrDefault(i => !i.IsHeader);
                    if (first != null)
                    {
                        ResolutionExactBox.SelectedItem = first;
                        item = first;
                    }
                }
                finally
                {
                    syncingResolutionRadios = false;
                }
            }

            if (item?.Mode != null)
            {
                settings.SetPreferredResolutionForMode(editingLaunchMode, item.Width, item.Height);
            }

            UpdateResolutionUnavailableHint(item);
        }

        private void SyncNativeHdrMigrationStatus()
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (NativeHdrMigrationStatusText == null || plugin == null)
            {
                return;
            }

            var enabled = GetCachedNativeHdrEnabledCount(plugin);
            var backup = plugin.CountNativeHdrBackupIds();
            var format = TryFindResource("LOCDisplayManager_NativeHdrStatusFormat") as string
                ?? "{0} game(s) still have EnableSystemHdr on · backup: {1} id(s).";
            NativeHdrMigrationStatusText.Text = string.Format(format, enabled, backup);
        }

        private void NativeHdrClearNow_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (plugin == null)
            {
                return;
            }

            var result = plugin.RunNativeHdrMigration();
            cachedNativeHdrEnabledCount = null;
            if (!result.Success)
            {
                NativeHdrMigrationStatusText.Text = result.Error ?? "Migration failed.";
                return;
            }

            var format = TryFindResource("LOCDisplayManager_NativeHdrClearedFormat") as string
                ?? "Cleared {0} game(s). Remaining with flag on: {1}.";
            NativeHdrMigrationStatusText.Text = string.Format(format, result.ClearedCount, result.RemainingEnabledCount);
            UpdateOverview();
        }

        private void NativeHdrRestore_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (plugin == null)
            {
                return;
            }

            var confirm = TryFindResource("LOCDisplayManager_NativeHdrRestoreConfirm") as string
                ?? "Restore EnableSystemHdr=true for games in the Display Manager backup? Native and NX HDR may stack again.";
            if (MessageBox.Show(confirm, "Display Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var result = plugin.RestoreNativeHdrFromBackup();
            cachedNativeHdrEnabledCount = null;
            if (!result.Success)
            {
                NativeHdrMigrationStatusText.Text = result.Error ?? "Restore failed.";
                return;
            }

            var format = TryFindResource("LOCDisplayManager_NativeHdrRestoredFormat") as string
                ?? "Restored {0} game(s). Games with flag on now: {1}.";
            NativeHdrMigrationStatusText.Text = string.Format(format, result.ClearedCount, result.RemainingEnabledCount);
            UpdateOverview();
        }

        private void OpenSetupWizard_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            settings?.Plugin?.OpenSetupWizard();
            UpdateOverview();
        }

        private static string BuildDisplayPillLabel(DisplayInfo display)
        {
            var label = display.EffectiveName;
            if (display.IsPrimary)
            {
                label += " · primary";
            }

            if (!display.IsConnected)
            {
                label += " · offline";
            }
            else if (display.Width > 0 && display.Height > 0)
            {
                label += $" · {display.Width}×{display.Height}";
                if (display.RefreshRateHz > 0)
                {
                    label += $"@{display.RefreshRateHz:0.#}";
                }
            }

            return label;
        }

        private Border CreatePill(string text)
        {
            var border = new Border
            {
                Style = TryFindResource("CapabilityPill") as Style
                    ?? TryFindResource("SummaryPill") as Style,
                Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            return border;
        }

        private Border CreateStatusBadge(string label, string value, string brushKey, double opacity = 1.0)
        {
            var safeValue = string.IsNullOrWhiteSpace(value) ? "-" : value;
            var displayText = string.IsNullOrWhiteSpace(label)
                ? safeValue
                : string.Format("{0}: {1}", label, safeValue);
            var text = new TextBlock { Text = displayText };
            var badge = new Border
            {
                Style = TryFindResource("DeviceStatusPill") as Style
                    ?? TryFindResource("StatusPill") as Style
                    ?? TryFindResource("SummaryPill") as Style,
                Child = text
            };
            ApplyStatusBadgeAppearance(text, brushKey, opacity);
            return badge;
        }

        private static void ApplyStatusBadgeAppearance(TextBlock textBlock, string brushKey, double opacity = 1.0)
        {
            if (textBlock == null || string.IsNullOrWhiteSpace(brushKey))
            {
                return;
            }

            textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            textBlock.Opacity = 1.0;

            var badge = textBlock.Parent as Border;
            if (badge == null)
            {
                for (var parent = VisualTreeHelper.GetParent(textBlock);
                     parent != null;
                     parent = VisualTreeHelper.GetParent(parent))
                {
                    badge = parent as Border;
                    if (badge != null)
                    {
                        break;
                    }
                }
            }

            if (badge == null)
            {
                return;
            }

            badge.BorderThickness = new Thickness(0);
            badge.BorderBrush = Brushes.Transparent;
            badge.Effect = null;
            badge.Opacity = opacity;

            string backgroundKey;
            if (string.Equals(brushKey, "PositiveRatingBrush", StringComparison.Ordinal))
            {
                backgroundKey = "Narian.BadgeSuccessBg";
            }
            else if (string.Equals(brushKey, "WarningBrush", StringComparison.Ordinal))
            {
                backgroundKey = "Narian.BadgeWarningBg";
            }
            else
            {
                backgroundKey = "Narian.BadgeMutedBg";
            }

            badge.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        }

        private void RebuildDisplayCards()
        {
            if (DisplayCardsPanel == null)
            {
                return;
            }

            DisplayCardsPanel.Children.Clear();
            var settings = DataContext as DisplayManagerSettings;
            var displays = settings?.AvailableDisplays;
            if (displays == null || displays.Count == 0)
            {
                DisplayCardsPanel.Children.Add(new TextBlock
                {
                    Text = TryFindResource("LOCDisplayManager_OverviewDisplaysNone") as string
                        ?? "No displays detected.",
                    Style = TryFindResource("HintText") as Style
                });
                return;
            }

            var grid = new UniformGrid
            {
                Columns = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            for (var index = 0; index < displays.Count; index++)
            {
                grid.Children.Add(CreateDisplayCard(displays[index], index));
            }

            DisplayCardsPanel.Children.Add(grid);
        }

        private UIElement CreateDisplayCard(DisplayInfo display, int index)
        {
            var card = new Border
            {
                Style = TryFindResource("DeviceCard") as Style
                    ?? TryFindResource("SummaryCard") as Style,
                Margin = new Thickness(index % 2 == 0 ? 0 : 8, 0, index % 2 == 0 ? 8 : 0, 16),
                Cursor = Cursors.Arrow
            };

            var root = new StackPanel();
            var title = new TextBlock
            {
                Style = TryFindResource("SummaryCardTitle") as Style,
                TextWrapping = TextWrapping.Wrap,
                Text = display.Name
            };
            root.Children.Add(new Border
            {
                Style = TryFindResource("SummaryTitleSeparator") as Style,
                Child = title
            });

            var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            if (display.IsConnected)
            {
                pills.Children.Add(CreateStatusBadge(
                    null,
                    TryFindResource("LOCDisplayManager_StatusConnected") as string ?? "Connected",
                    "PositiveRatingBrush"));
            }
            else
            {
                pills.Children.Add(CreateStatusBadge(
                    null,
                    TryFindResource("LOCDisplayManager_StatusDisconnected") as string ?? "Disconnected",
                    "GlyphBrush",
                    0.9));
            }

            var settingsForBadges = DataContext as DisplayManagerSettings;
            if (IsConfiguredPlayPrimaryForMode(display, settingsForBadges, ApplicationMode.Desktop))
            {
                pills.Children.Add(CreateStatusBadge(
                    null,
                    TryFindResource("LOCDisplayManager_LaunchModeDesktop") as string ?? "Desktop",
                    "PositiveRatingBrush"));
            }

            if (IsConfiguredPlayPrimaryForMode(display, settingsForBadges, ApplicationMode.Fullscreen))
            {
                pills.Children.Add(CreateStatusBadge(
                    null,
                    TryFindResource("LOCDisplayManager_LaunchModeFullscreen") as string ?? "Fullscreen",
                    "PositiveRatingBrush"));
            }

            if (display.IsConnected)
            {
                var settings = DataContext as DisplayManagerSettings;
                var hdrSupported = ProbeDisplayHdrSupported(settings, display);
                pills.Children.Add(CreateStatusBadge(
                    null,
                    hdrSupported
                        ? (TryFindResource("LOCDisplayManager_StatusHdrSupported") as string ?? "HDR supported")
                        : (TryFindResource("LOCDisplayManager_StatusHdrNotSupported") as string ?? "No HDR"),
                    hdrSupported ? "PositiveRatingBrush" : "GlyphBrush",
                    hdrSupported ? 1.0 : 0.9));
            }

            pills.Children.Add(CreatePill(display.IdentityKind == "edid"
                ? (TryFindResource("LOCDisplayManager_IdentityEdid") as string ?? "EDID identity")
                : (TryFindResource("LOCDisplayManager_IdentityConnector") as string ?? "Connector fallback")));

            if (display.IsConnected && display.Width > 0 && display.Height > 0)
            {
                var mode = $"{display.Width}×{display.Height}";
                if (display.RefreshRateHz > 0)
                {
                    mode += $" @ {display.RefreshRateHz:0.#} Hz";
                }

                pills.Children.Add(CreatePill(mode));
            }

            root.Children.Add(pills);

            var aliasLabel = new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_DisplayAlias") as string ?? "Custom name",
                Style = TryFindResource("FieldLabel") as Style
            };
            root.Children.Add(aliasLabel);

            var aliasRow = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 0, 0, 4)
            };

            if (display.IsConnected)
            {
                var identifyTooltip = TryFindResource("LOCDisplayManager_IdentifyDisplay") as string
                    ?? "Identify display";
                var identifyButton = new Button
                {
                    Style = TryFindResource("IconSquareButton") as Style,
                    Width = 36,
                    Height = 36,
                    MinWidth = 36,
                    MinHeight = 36,
                    MaxWidth = 36,
                    MaxHeight = 36,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = identifyTooltip,
                    Cursor = Cursors.Hand,
                    Content = new TextBlock
                    {
                        Text = "\uE7B3",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                DockPanel.SetDock(identifyButton, Dock.Right);
                var displayCapture = display;
                identifyButton.Click += (_, __) => IdentifyDisplay(displayCapture);
                aliasRow.Children.Add(identifyButton);
            }

            var aliasBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(display.CustomName) ? (display.Name ?? string.Empty) : display.CustomName,
                MinHeight = 36,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            aliasBox.TextChanged += (_, __) =>
            {
                var text = aliasBox.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)
                    || string.Equals(text.Trim(), display.Name?.Trim(), StringComparison.Ordinal))
                {
                    display.CustomName = null;
                }
                else
                {
                    display.CustomName = text;
                }

                UpdateOverview();
                RefreshPlayDisplayBoxes();
                RefreshMissingDisplayAndTurnOffUi();
            };
            aliasRow.Children.Add(aliasBox);
            root.Children.Add(aliasRow);

            var aliasHelp = new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_DisplayAliasHelp") as string
                    ?? "Optional Playnite name. Disconnected displays with a custom name stay listed.",
                Style = TryFindResource("HintText") as Style,
                Margin = new Thickness(0, 0, 0, 0)
            };
            root.Children.Add(aliasHelp);

            card.Child = root;
            return card;
        }

        private void IdentifyDisplay(DisplayInfo display)
        {
            var primary = TryFindResource("LOCDisplayManager_StatusPrimary") as string ?? "Primary";
            DisplayIdentifyOverlay.Show(display, primary);
        }

        private void GameProfilesSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildGameProfileRows();
        }

        private void RebuildGameProfileRows()
        {
            if (GameProfileRowsPanel == null || NoGameProfilesText == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            GameProfileRowsPanel.Children.Clear();
            var profiles = settings?.AvailableGameProfiles?
                .OrderBy(profile => profile.GameName, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                ?? new System.Collections.Generic.List<GameDisplayProfileEntry>();

            var filter = GameProfilesSearchBox?.Text?.Trim() ?? string.Empty;
            var filtered = string.IsNullOrEmpty(filter)
                ? profiles
                : profiles
                    .Where(profile =>
                        !string.IsNullOrWhiteSpace(profile.GameName)
                        && profile.GameName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    .ToList();

            NoGameProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (NoGameProfilesFilterText != null)
            {
                NoGameProfilesFilterText.Visibility =
                    profiles.Count > 0 && filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GameProfilesSearchBox != null)
            {
                GameProfilesSearchBox.IsEnabled = profiles.Count > 0;
            }

            foreach (var profile in filtered)
            {
                GameProfileRowsPanel.Children.Add(CreateGameProfileRow(profile, settings));
            }
        }

        private UIElement CreateGameProfileRow(GameDisplayProfileEntry profile, DisplayManagerSettings settings)
        {
            var container = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 16),
                Margin = new Thickness(0, 0, 0, 20),
                Cursor = Cursors.Arrow
            };
            container.SetResourceReference(Border.BorderBrushProperty, "GlyphBrush");

            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
            var removeButton = new Button
            {
                Content = TryFindResource("LOCDisplayManager_RemoveProfile") as string ?? "Remove",
                MinWidth = 90,
                MinHeight = 32,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            removeButton.Click += (_, __) =>
            {
                var confirmed = settings?.Plugin != null
                    && settings.Plugin.ConfirmRemoveGameProfile(profile.GameName);
                if (!confirmed)
                {
                    return;
                }

                settings.AvailableGameProfiles.Remove(profile);
                RebuildGameProfileRows();
            };
            DockPanel.SetDock(removeButton, Dock.Right);
            header.Children.Add(removeButton);
            header.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(profile.GameName)
                    ? (TryFindResource("LOCDisplayManager_UnknownGame") as string ?? "Unknown game")
                    : profile.GameName,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            body.Children.Add(header);

            var controls = new Grid();
            for (var i = 0; i < 4; i++)
            {
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var editors = new[]
            {
                CreateGameProfileDisplayEditor(profile, settings),
                CreateGameProfileHdrEditor(profile),
                CreateGameProfileRefreshEditor(profile, settings),
                CreateGameProfileResolutionEditor(profile, settings)
            };
            for (var i = 0; i < editors.Length; i++)
            {
                var editor = editors[i];
                if (editor is FrameworkElement fe)
                {
                    fe.Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0);
                    fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                }

                Grid.SetColumn(editor, i);
                controls.Children.Add(editor);
            }

            body.Children.Add(controls);
            container.Child = body;
            return container;
        }

        private UIElement CreateGameProfileDisplayEditor(
            GameDisplayProfileEntry profile,
            DisplayManagerSettings settings)
        {
            var choices = new List<GameProfileChoice>
            {
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameDisplayInherit") as string
                        ?? "Keep global settings",
                    DisplayId = null,
                    HasDisplayId = false
                },
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_PlayDisplayWindowsDefault") as string
                        ?? "Keep Windows default",
                    DisplayId = string.Empty,
                    HasDisplayId = true
                }
            };

            foreach (var display in settings?.AvailableDisplays?.Where(d => d.IsConnected)
                     ?? Enumerable.Empty<DisplayInfo>())
            {
                choices.Add(new GameProfileChoice
                {
                    Label = display.EffectiveName,
                    DisplayId = display.Id,
                    HasDisplayId = true
                });
            }

            GameProfileChoice selected;
            if (profile.PreferredPlayDisplayId == null)
            {
                selected = choices[0];
            }
            else if (string.IsNullOrEmpty(profile.PreferredPlayDisplayId))
            {
                selected = choices[1];
            }
            else
            {
                selected = choices.FirstOrDefault(c =>
                    c.HasDisplayId
                    && string.Equals(c.DisplayId, profile.PreferredPlayDisplayId, StringComparison.OrdinalIgnoreCase))
                    ?? choices[0];
            }

            return CreateLabeledProfileCombo(
                TryFindResource("LOCDisplayManager_GameMenuDisplaySection") as string ?? "Display",
                choices,
                selected,
                choice =>
                {
                    profile.PreferredPlayDisplayId = choice.HasDisplayId ? choice.DisplayId : null;
                    var result = SanitizeGameProfileEntryForPlayDisplay(profile, settings);
                    if (result != null && result.Changed)
                    {
                        settings?.Plugin?.ShowOverrideResetMessage(result);
                    }
                });
        }

        private DisplayOverrideSanitizeResult SanitizeGameProfileEntryForPlayDisplay(
            GameDisplayProfileEntry profile,
            DisplayManagerSettings settings)
        {
            if (profile == null)
            {
                return new DisplayOverrideSanitizeResult();
            }

            var connected = settings?.AvailableDisplays?
                .Where(d => d.IsConnected)
                .ToList() ?? new List<DisplayInfo>();

            DisplayInfo target = null;
            if (profile.PreferredPlayDisplayId == null)
            {
                target = ResolvePreferredPlayDisplay(settings, connected);
            }
            else if (string.IsNullOrEmpty(profile.PreferredPlayDisplayId))
            {
                target = connected.FirstOrDefault(d => d.IsPrimary) ?? connected.FirstOrDefault();
            }
            else
            {
                target = connected.FirstOrDefault(d =>
                    string.Equals(d.Id, profile.PreferredPlayDisplayId, StringComparison.OrdinalIgnoreCase));
            }

            if (target == null)
            {
                return new DisplayOverrideSanitizeResult();
            }

            return DisplayOverrideSanitizer.SanitizeGameProfileEntry(
                profile,
                target,
                settings?.Plugin?.Resolutions,
                settings?.Plugin?.RefreshRates,
                display => ProbeDisplayHdrSupported(settings, display));
        }

        private UIElement CreateGameProfileHdrEditor(GameDisplayProfileEntry profile)
        {
            var choices = new List<GameProfileChoice>
            {
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameHdrInherit") as string ?? "Keep global settings",
                    Hdr = GameHdrOverride.Inherit
                },
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameHdrForceOn") as string ?? "Always turn HDR on",
                    Hdr = GameHdrOverride.ForceOn
                },
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameHdrForceOff") as string ?? "Always turn HDR off",
                    Hdr = GameHdrOverride.ForceOff
                }
            };

            var selected = choices.FirstOrDefault(c => c.Hdr == profile.HdrOverride) ?? choices[0];
            return CreateLabeledProfileCombo(
                TryFindResource("LOCDisplayManager_GameMenuHdrSection") as string ?? "HDR",
                choices,
                selected,
                choice =>
                {
                    profile.HdrOverride = choice.Hdr;
                });
        }

        private UIElement CreateGameProfileRefreshEditor(
            GameDisplayProfileEntry profile,
            DisplayManagerSettings settings)
        {
            var choices = new List<GameProfileChoice>
            {
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameHzInherit") as string ?? "Keep global settings",
                    Refresh = GameRefreshRateOverride.Inherit
                },
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameHzNative") as string ?? "Native",
                    Refresh = GameRefreshRateOverride.Native
                }
            };

            var primary = ResolvePrimaryDisplayForEditors(settings);
            var rates = GetCachedAvailableRates(settings, primary);
            var exactFormat = TryFindResource("LOCDisplayManager_RefreshPolicyExactFormat") as string
                ?? "{0:0.###} Hz";
            foreach (var rate in rates)
            {
                choices.Add(new GameProfileChoice
                {
                    Label = string.Format(exactFormat, rate),
                    Refresh = GameRefreshRateOverride.ExactHz,
                    Hz = rate
                });
            }

            choices.Add(new GameProfileChoice
            {
                Label = TryFindResource("LOCDisplayManager_GameHzHighest") as string ?? "Highest available",
                Refresh = GameRefreshRateOverride.HighestDetected
            });

            GameProfileChoice selected;
            if (profile.RefreshRateOverride == GameRefreshRateOverride.ExactHz
                || profile.RefreshRateOverride == GameRefreshRateOverride.Prefer60
                || profile.RefreshRateOverride == GameRefreshRateOverride.Prefer120)
            {
                selected = choices.FirstOrDefault(c =>
                    c.Refresh == GameRefreshRateOverride.ExactHz
                    && c.Hz.HasValue
                    && profile.PreferredRefreshRateHz.HasValue
                    && Math.Abs(c.Hz.Value - profile.PreferredRefreshRateHz.Value) < 0.05)
                    ?? choices[0];
            }
            else
            {
                selected = choices.FirstOrDefault(c => c.Refresh == profile.RefreshRateOverride) ?? choices[0];
            }

            return CreateLabeledProfileCombo(
                TryFindResource("LOCDisplayManager_GameMenuHzSection") as string ?? "Refresh rate",
                choices,
                selected,
                choice =>
                {
                    profile.RefreshRateOverride = choice.Refresh;
                    profile.PreferredRefreshRateHz = choice.Refresh == GameRefreshRateOverride.ExactHz
                        ? choice.Hz
                        : null;
                });
        }

        private UIElement CreateGameProfileResolutionEditor(
            GameDisplayProfileEntry profile,
            DisplayManagerSettings settings)
        {
            var choices = new List<GameProfileChoice>
            {
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameResolutionInherit") as string
                        ?? "Keep global settings",
                    Resolution = GameResolutionOverride.Inherit
                },
                new GameProfileChoice
                {
                    Label = TryFindResource("LOCDisplayManager_GameResolutionNative") as string
                        ?? "Native",
                    Resolution = GameResolutionOverride.Native
                }
            };

            var primary = ResolvePrimaryDisplayForEditors(settings);
            var modes = PrepareResolutionModesForUi(GetCachedAvailableModes(settings, primary));
            var customSuffix = TryFindResource("LOCDisplayManager_ResolutionCustomSuffix") as string
                ?? "custom";
            foreach (var mode in modes)
            {
                choices.Add(new GameProfileChoice
                {
                    Label = mode.IsCustom
                        ? mode.Label + " (" + customSuffix + ")"
                        : mode.Label,
                    Resolution = GameResolutionOverride.Exact,
                    Width = mode.Width,
                    Height = mode.Height
                });
            }

            choices.Add(new GameProfileChoice
            {
                Label = TryFindResource("LOCDisplayManager_GameResolutionLowest") as string
                    ?? "Lowest available",
                Resolution = GameResolutionOverride.LowestAvailable
            });
            choices.Add(new GameProfileChoice
            {
                Label = TryFindResource("LOCDisplayManager_GameResolutionHighest") as string
                    ?? "Highest available",
                Resolution = GameResolutionOverride.HighestAvailable
            });

            GameProfileChoice selected;
            if (profile.ResolutionOverride == GameResolutionOverride.Exact)
            {
                selected = choices.FirstOrDefault(c =>
                    c.Resolution == GameResolutionOverride.Exact
                    && c.Width == profile.PreferredResolutionWidth
                    && c.Height == profile.PreferredResolutionHeight)
                    ?? choices[0];
            }
            else
            {
                selected = choices.FirstOrDefault(c => c.Resolution == profile.ResolutionOverride)
                    ?? choices[0];
            }

            return CreateLabeledProfileCombo(
                TryFindResource("LOCDisplayManager_GameMenuResolutionSection") as string ?? "Resolution",
                choices,
                selected,
                choice =>
                {
                    profile.ResolutionOverride = choice.Resolution;
                    if (choice.Resolution == GameResolutionOverride.Exact)
                    {
                        profile.PreferredResolutionWidth = choice.Width;
                        profile.PreferredResolutionHeight = choice.Height;
                    }
                    else
                    {
                        profile.PreferredResolutionWidth = null;
                        profile.PreferredResolutionHeight = null;
                    }
                });
        }

        private DisplayInfo ResolvePrimaryDisplayForEditors(DisplayManagerSettings settings)
        {
            var preferredId = settings?.PreferredPlayDisplayId;
            var displays = settings?.AvailableDisplays;
            if (displays == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                var preferred = displays.FirstOrDefault(d =>
                    d.IsConnected
                    && string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
                if (preferred != null)
                {
                    return preferred;
                }
            }

            return displays.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? displays.FirstOrDefault(d => d.IsConnected);
        }

        private UIElement CreateLabeledProfileCombo(
            string label,
            IList<GameProfileChoice> choices,
            GameProfileChoice selected,
            Action<GameProfileChoice> onChanged)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                Opacity = 0.85
            });

            var box = new ComboBox
            {
                MinHeight = 36,
                DisplayMemberPath = "Label",
                ItemsSource = choices,
                SelectedItem = selected,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            box.SelectionChanged += (_, __) =>
            {
                if (!(box.SelectedItem is GameProfileChoice choice) || onChanged == null)
                {
                    return;
                }

                onChanged(choice);
            };
            panel.Children.Add(box);
            return panel;
        }

        private sealed class GameProfileChoice
        {
            public string Label { get; set; }

            public bool HasDisplayId { get; set; }

            public string DisplayId { get; set; }

            public GameHdrOverride Hdr { get; set; }

            public GameRefreshRateOverride Refresh { get; set; }

            public double? Hz { get; set; }

            public GameResolutionOverride Resolution { get; set; }

            public int? Width { get; set; }

            public int? Height { get; set; }
        }

        private void RebuildPlatformProfileRows()
        {
            if (PlatformProfileRowsPanel == null || NoPlatformProfilesText == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            PlatformProfileRowsPanel.Children.Clear();
            RefreshPlatformAddBox(settings);

            var profiles = settings?.AvailablePlatformProfiles?
                .OrderBy(p => p.PlatformName, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                ?? new System.Collections.Generic.List<PlatformProfileEntry>();

            NoPlatformProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var profile in profiles)
            {
                PlatformProfileRowsPanel.Children.Add(CreatePlatformProfileRow(profile, settings));
            }
        }

        private void RefreshPlatformAddBox(DisplayManagerSettings settings)
        {
            if (PlatformAddBox == null || settings?.Plugin?.PlayniteApi?.Database?.Platforms == null)
            {
                return;
            }

            var existing = new HashSet<Guid>(
                (settings.AvailablePlatformProfiles ?? new System.Collections.Generic.List<PlatformProfileEntry>())
                    .Select(p => p.PlatformId));
            var platforms = settings.Plugin.PlayniteApi.Database.Platforms
                .Where(p => p != null && !existing.Contains(p.Id))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            PlatformAddBox.ItemsSource = platforms;
            if (platforms.Count > 0)
            {
                PlatformAddBox.SelectedIndex = 0;
            }
        }

        private void PlatformProfileAdd_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || !(PlatformAddBox?.SelectedValue is Guid platformId) || platformId == Guid.Empty)
            {
                return;
            }

            if (settings.AvailablePlatformProfiles.Any(p => p.PlatformId == platformId))
            {
                return;
            }

            var platform = settings.Plugin?.PlayniteApi?.Database?.Platforms?
                .FirstOrDefault(p => p.Id == platformId);
            var defaults = settings.GetDefaultDisplayProfile();
            settings.AvailablePlatformProfiles.Add(new PlatformProfileEntry
            {
                PlatformId = platformId,
                PlatformName = platform?.Name ?? platformId.ToString(),
                DisplayProfileId = defaults?.Id
            });
            RebuildPlatformProfileRows();
        }

        private UIElement CreatePlatformProfileRow(PlatformProfileEntry profile, DisplayManagerSettings settings)
        {
            var container = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 8, 16),
                Margin = new Thickness(0, 0, 0, 24),
                Cursor = Cursors.Arrow
            };
            container.SetResourceReference(Border.BorderBrushProperty, "GlyphBrush");

            var content = new StackPanel();
            var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(profile.PlatformName)
                    ? (TryFindResource("LOCDisplayManager_UnknownPlatform") as string ?? "Unknown platform")
                    : profile.PlatformName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeButton = new Button
            {
                Content = TryFindResource("LOCDisplayManager_RemoveProfile") as string ?? "Remove",
                MinWidth = 90,
                MinHeight = 36,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                Style = TryFindResource("NarianPrimaryButton") as Style
            };
            removeButton.Click += (_, __) =>
            {
                var confirmed = settings?.Plugin != null
                    && settings.Plugin.ConfirmRemovePlatformProfile(profile.PlatformName);
                if (!confirmed)
                {
                    return;
                }

                settings.AvailablePlatformProfiles.Remove(profile);
                RebuildPlatformProfileRows();
            };
            Grid.SetColumn(removeButton, 1);
            titleRow.Children.Add(removeButton);
            content.Children.Add(titleRow);

            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_GameMenuHdrSection") as string ?? "HDR",
                FormatHdrOverrideSummary(profile.HdrOverride)));
            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_GameMenuDisplaySection") as string ?? "Display",
                FormatPlayDisplayOverrideSummary(profile.PreferredPlayDisplayId, settings)));
            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_GameMenuHzSection") as string ?? "Refresh rate",
                FormatRefreshOverrideSummary(profile.RefreshRateOverride, profile.PreferredRefreshRateHz)));
            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_GameMenuResolutionSection") as string ?? "Resolution",
                FormatResolutionOverrideSummary(
                    profile.ResolutionOverride,
                    profile.PreferredResolutionWidth,
                    profile.PreferredResolutionHeight)));

            container.Child = content;
            return container;
        }

        private static TextBlock CreateProfileSummaryLine(string label, string value)
        {
            return new TextBlock
            {
                Text = string.Format("{0}: {1}", label, value),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.9
            };
        }

        private string FormatHdrOverrideSummary(GameHdrOverride value)
        {
            switch (value)
            {
                case GameHdrOverride.ForceOn:
                    return TryFindResource("LOCDisplayManager_GameHdrForceOn") as string ?? "Force HDR on";
                case GameHdrOverride.ForceOff:
                    return TryFindResource("LOCDisplayManager_GameHdrForceOff") as string ?? "Force HDR off (SDR)";
                case GameHdrOverride.DoNotTouch:
                    return TryFindResource("LOCDisplayManager_GameHdrDoNotTouch") as string ?? "Do not touch HDR";
                default:
                    return TryFindResource("LOCDisplayManager_GameHdrInherit") as string ?? "Keep global settings";
            }
        }

        private string FormatPlayDisplayOverrideSummary(
            string preferredPlayDisplayId,
            DisplayManagerSettings settings)
        {
            if (preferredPlayDisplayId == null)
            {
                return TryFindResource("LOCDisplayManager_GameDisplayInherit") as string
                    ?? "Keep global settings";
            }

            if (string.IsNullOrEmpty(preferredPlayDisplayId))
            {
                return TryFindResource("LOCDisplayManager_PlayDisplayWindowsDefault") as string
                    ?? "Keep Windows default";
            }

            var match = settings?.AvailableDisplays?.FirstOrDefault(d =>
                string.Equals(d.Id, preferredPlayDisplayId, StringComparison.OrdinalIgnoreCase));
            return match?.EffectiveName ?? preferredPlayDisplayId;
        }

        private string FormatRefreshOverrideSummary(GameRefreshRateOverride value, double? preferredHz = null)
        {
            switch (value)
            {
                case GameRefreshRateOverride.Native:
                    return TryFindResource("LOCDisplayManager_GameHzNative") as string ?? "Force native";
                case GameRefreshRateOverride.ExactHz:
                    if (preferredHz.HasValue)
                    {
                        var format = TryFindResource("LOCDisplayManager_RefreshPolicyExactFormat") as string
                            ?? "{0:0.###} Hz";
                        return string.Format(format, preferredHz.Value);
                    }

                    return TryFindResource("LOCDisplayManager_RefreshPolicyExact") as string ?? "Exact Hz";
                case GameRefreshRateOverride.Prefer60:
                    return TryFindResource("LOCDisplayManager_GameHz60") as string ?? "Prefer ~60 Hz";
                case GameRefreshRateOverride.Prefer120:
                    return TryFindResource("LOCDisplayManager_GameHz120") as string ?? "Prefer ~120 Hz";
                case GameRefreshRateOverride.HighestDetected:
                    return TryFindResource("LOCDisplayManager_GameHzHighest") as string ?? "Highest detected";
                default:
                    return TryFindResource("LOCDisplayManager_GameHzInherit") as string ?? "Keep global settings";
            }
        }

        private string FormatResolutionOverrideSummary(
            GameResolutionOverride value,
            int? preferredWidth = null,
            int? preferredHeight = null)
        {
            switch (value)
            {
                case GameResolutionOverride.Native:
                    return TryFindResource("LOCDisplayManager_GameResolutionNative") as string
                        ?? "Native: do not change resolution";
                case GameResolutionOverride.Exact:
                    if (preferredWidth > 0 && preferredHeight > 0)
                    {
                        var format = TryFindResource("LOCDisplayManager_ResolutionPolicyExactFormat") as string
                            ?? "{0} × {1}";
                        return string.Format(format, preferredWidth.Value, preferredHeight.Value);
                    }

                    return TryFindResource("LOCDisplayManager_ResolutionPolicyExact") as string
                        ?? "Exact resolution";
                case GameResolutionOverride.LowestAvailable:
                    return TryFindResource("LOCDisplayManager_GameResolutionLowest") as string
                        ?? "Lowest available";
                case GameResolutionOverride.HighestAvailable:
                    return TryFindResource("LOCDisplayManager_GameResolutionHighest") as string
                        ?? "Highest available";
                default:
                    return TryFindResource("LOCDisplayManager_GameResolutionInherit") as string
                        ?? "Keep global settings";
            }
        }

        private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // Best-effort navigation from About links.
            }

            e.Handled = true;
        }

        private void OpenExternalButton(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var url = button?.Tag as string;
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Best-effort navigation from About buttons.
            }
        }

        private static string GetInstalledVersion()
        {
            try
            {
                var version = typeof(DisplayManagerSettingsView).Assembly.GetName().Version;
                if (version == null)
                {
                    return "1.0.0";
                }

                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }
}
