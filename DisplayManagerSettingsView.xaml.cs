using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Profiles;
using PlayniteDisplayManager.Refresh;

namespace PlayniteDisplayManager
{
    public partial class DisplayManagerSettingsView : UserControl
    {
        private readonly bool themeStandaloneWindow;
        private ScrollViewer hostScrollViewer;
        private Window hostWindow;
        private PlayniteDisplayManagerPlugin subscribedPlugin;
        private DisplaySnapshot topologyTrialSnapshot;
        private DispatcherTimer topologyTrialTimer;
        private int topologyTrialSecondsLeft;
        private bool syncingRefreshRadios;

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

            DataContextChanged += (_, __) =>
            {
                SubscribeDisplaysChanged();
                ApplyAppearancePreset();
                BuildAppearancePresetChips();
                RebuildDisplayCards();
                RebuildGameProfileRows();
                RefreshTopologyTargetBox();
                SyncHdrPolicyRadios();
                SyncHdrMetadataControls();
                SyncRefreshRateRadios();
                SyncDesktopAccessControls();
                SyncAudioSwitcherControls();
                UpdateOverview();
            };
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            ApplyAppearancePreset();
            BuildAppearancePresetChips();
            ApplyPreferredWindowSize();
            AttachToHost();
            Dispatcher.BeginInvoke(new Action(AttachToHost), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(AttachToHost), DispatcherPriority.ApplicationIdle);
            Dispatcher.BeginInvoke(new Action(FillSelectedContentHosts), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(FillSelectedContentHosts), DispatcherPriority.ApplicationIdle);
            RebuildDisplayCards();
            RebuildGameProfileRows();
            RefreshTopologyTargetBox();
            SyncHdrPolicyRadios();
            SyncHdrMetadataControls();
            SyncRefreshRateRadios();
            SyncDesktopAccessControls();
            SyncAudioSwitcherControls();
            UpdateOverview();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
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
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RebuildDisplayCards();
                    RefreshTopologyTargetBox();
                    UpdateOverview();
                }));
                return;
            }

            RebuildDisplayCards();
            RefreshTopologyTargetBox();
            UpdateOverview();
        }

        private void ApplyAppearancePreset()
        {
            var settings = DataContext as DisplayManagerSettings;
            var preset = settings != null
                ? settings.AppearancePreset
                : SettingsAppearance.Midnight;
            SettingsAppearance.Apply(this, preset);

            if (themeStandaloneWindow)
            {
                SettingsAppearance.ApplyWindow(Window.GetWindow(this), preset);
            }

            RefreshAppearancePresetChips();
        }

        private void RootTabsSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FillSelectedContentHosts();
                if (IsGameProfilesTabSelected())
                {
                    RebuildGameProfileRows();
                }
            }), DispatcherPriority.Loaded);
        }

        private bool IsGameProfilesTabSelected()
        {
            // Overview, Displays, HDR, Game profiles, …
            return RootTabs != null && RootTabs.SelectedIndex == 3;
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
                hostWindow = null;
            }
        }

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs args)
        {
            ApplyViewportSize();
            FillSelectedContentHosts();
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
            if (window == null)
            {
                return;
            }

            window.SizeToContent = SizeToContent.Manual;
            if (window.MinWidth < 1000)
            {
                window.MinWidth = 1000;
            }
            if (window.MinHeight < 700)
            {
                window.MinHeight = 700;
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

        private void BuildAppearancePresetChips()
        {
            if (AppearancePresetChips == null)
            {
                return;
            }

            AppearancePresetChips.Children.Clear();
            var settings = DataContext as DisplayManagerSettings;
            var options = settings != null ? settings.AppearancePresetOptions : null;
            if (options == null)
            {
                return;
            }

            foreach (var option in options)
            {
                var button = new Button
                {
                    Content = option.DisplayName,
                    Tag = option.Value,
                    MinHeight = 36,
                    MinWidth = 88,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(12, 6, 12, 6),
                    Cursor = Cursors.Hand
                };
                button.Click += AppearancePresetChip_OnClick;
                button.MouseEnter += AppearancePresetChip_OnMouseEnter;
                button.MouseLeave += AppearancePresetChip_OnMouseLeave;
                AppearancePresetChips.Children.Add(button);
            }

            RefreshAppearancePresetChips();
        }

        private void AppearancePresetChip_OnMouseEnter(object sender, MouseEventArgs e)
        {
            var button = sender as Button;
            var settings = DataContext as DisplayManagerSettings;
            if (button == null || settings == null)
            {
                return;
            }

            var selected = SettingsAppearance.Normalize(settings.AppearancePreset);
            if (string.Equals(button.Tag as string, selected, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var palette = SettingsAppearance.GetPalette(selected);
            button.Background = new SolidColorBrush(palette.Hover);
        }

        private void AppearancePresetChip_OnMouseLeave(object sender, MouseEventArgs e)
        {
            RefreshAppearancePresetChips();
        }

        private void AppearancePresetChip_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var preset = button == null ? null : button.Tag as string;
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || string.IsNullOrWhiteSpace(preset))
            {
                return;
            }

            settings.AppearancePreset = preset;
            ApplyAppearancePreset();
        }

        private void RefreshAppearancePresetChips()
        {
            if (AppearancePresetChips == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            var selected = settings != null
                ? SettingsAppearance.Normalize(settings.AppearancePreset)
                : SettingsAppearance.Midnight;
            var palette = SettingsAppearance.GetPalette(selected);
            var accent = new SolidColorBrush(palette.Accent);
            var accentOn = new SolidColorBrush(palette.AccentOn);
            var badgeBg = new SolidColorBrush(palette.BadgeBg);
            var text = new SolidColorBrush(palette.Text);
            accent.Freeze();
            accentOn.Freeze();
            badgeBg.Freeze();
            text.Freeze();

            foreach (var child in AppearancePresetChips.Children)
            {
                var button = child as Button;
                if (button == null)
                {
                    continue;
                }

                var isSelected = string.Equals(button.Tag as string, selected, StringComparison.OrdinalIgnoreCase);
                button.Background = isSelected ? accent : badgeBg;
                button.Foreground = isSelected ? accentOn : text;
                button.BorderBrush = isSelected ? accent : new SolidColorBrush(palette.Border);
                button.BorderThickness = new Thickness(1);
                button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
            }
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
            RebuildDisplayCards();
            RefreshTopologyTargetBox();
            UpdateOverview();
        }

        private void RefreshTopologyTargetBox()
        {
            if (TopologyTargetBox == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            var previous = TopologyTargetBox.SelectedValue as string;
            var connected = settings?.AvailableDisplays?
                .Where(d => d.IsConnected)
                .ToList() ?? new System.Collections.Generic.List<DisplayInfo>();
            TopologyTargetBox.ItemsSource = connected;
            if (connected.Count == 0)
            {
                TopologyTargetBox.SelectedIndex = -1;
                return;
            }

            if (!string.IsNullOrWhiteSpace(previous) &&
                connected.Any(d => string.Equals(d.Id, previous, StringComparison.OrdinalIgnoreCase)))
            {
                TopologyTargetBox.SelectedValue = previous;
            }
            else
            {
                var primary = connected.FirstOrDefault(d => d.IsPrimary) ?? connected[0];
                TopologyTargetBox.SelectedValue = primary.Id;
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

            var targetId = TopologyTargetBox?.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(targetId))
            {
                TopologyTrialStatusText.Text = TryFindResource("LOCDisplayManager_TopologyTrialNoTarget") as string
                    ?? "Select a connected display first.";
                return;
            }

            var makePrimary = TopologyMakePrimaryCheck?.IsChecked == true;
            var turnOffOthers = TopologyTurnOffOthersCheck?.IsChecked == true;
            if (!makePrimary && !turnOffOthers)
            {
                TopologyTrialStatusText.Text = TryFindResource("LOCDisplayManager_TopologyTrialNothing") as string
                    ?? "Enable make primary and/or turn off others.";
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
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            if (HdrPolicyAllRadio?.IsChecked == true)
            {
                settings.GlobalHdrPolicy = GlobalHdrPolicy.OnForAllGames;
            }
            else if (HdrPolicyMetadataRadio?.IsChecked == true)
            {
                settings.GlobalHdrPolicy = GlobalHdrPolicy.OnWhenMetadataIndicates;
            }
            else
            {
                settings.GlobalHdrPolicy = GlobalHdrPolicy.DoNotManage;
            }

            UpdateOverview();
        }

        private void SyncHdrPolicyRadios()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || HdrPolicyNoneRadio == null)
            {
                return;
            }

            switch (settings.GlobalHdrPolicy)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    HdrPolicyAllRadio.IsChecked = true;
                    break;
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    HdrPolicyMetadataRadio.IsChecked = true;
                    break;
                default:
                    HdrPolicyNoneRadio.IsChecked = true;
                    break;
            }
        }

        private void SyncHdrMetadataControls()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            if (HdrMetadataNamesBox != null)
            {
                HdrMetadataNamesBox.Text = settings.HdrMetadataMatchNamesText;
            }

            if (HdrIncludeTagsCheck != null)
            {
                HdrIncludeTagsCheck.IsChecked = settings.IncludeTagsInHdrMetadataMatch;
            }

            if (HdrOverrideCountText != null)
            {
                var count = settings.Plugin?.GetGameHdrOverrideCount() ?? 0;
                var format = TryFindResource("LOCDisplayManager_HdrOverrideCountFormat") as string
                    ?? "{0} game(s) with an HDR override (not inherit).";
                HdrOverrideCountText.Text = string.Format(format, count);
            }
        }

        private void HdrMetadataNamesBox_OnLostFocus(object sender, RoutedEventArgs e)
        {
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
            if (displays == null || displays.Count == 0)
            {
                OverviewDisplaysText.Text = TryFindResource("LOCDisplayManager_OverviewDisplaysNone") as string
                    ?? "No displays detected.";
                OverviewDisplayPills.Children.Clear();
            }
            else
            {
                var connected = displays.Count(d => d.IsConnected);
                var format = TryFindResource("LOCDisplayManager_OverviewDisplaysFormat") as string
                    ?? "{0} connected · primary: {1}";
                var primary = settings.PrimaryDisplayName
                    ?? (TryFindResource("LOCDisplayManager_StatusUnknown") as string ?? "Unknown");
                OverviewDisplaysText.Text = string.Format(format, connected, primary);

                OverviewDisplayPills.Children.Clear();
                foreach (var display in displays.Take(6))
                {
                    OverviewDisplayPills.Children.Add(CreatePill(BuildDisplayPillLabel(display)));
                }
            }

            if (OverviewPolicyText != null)
            {
                OverviewPolicyText.Text = settings?.Plugin?.GetHdrActionOverviewText()
                    ?? settings?.Plugin?.GetHdrPolicyOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_OverviewPolicyUnset") as string ?? "Not configured yet.");
            }

            if (OverviewPolicyPills != null)
            {
                OverviewPolicyPills.Children.Clear();
                var hdrLabel = TryFindResource("LOCDisplayManager_OverviewPolicy") as string ?? "HDR";
                var hdrValue = GetHdrPolicyBadgeValue(settings);
                var hdrBrush = settings?.GlobalHdrPolicy == GlobalHdrPolicy.DoNotManage
                    ? "GlyphBrush"
                    : "PositiveRatingBrush";
                var hdrOpacity = settings?.GlobalHdrPolicy == GlobalHdrPolicy.DoNotManage ? 0.65 : 1.0;
                OverviewPolicyPills.Children.Add(CreateStatusBadge(hdrLabel, hdrValue, hdrBrush, hdrOpacity));

                var refreshLabel = TryFindResource("LOCDisplayManager_OverviewRefreshRate") as string ?? "Refresh rate";
                var refreshValue = settings?.Plugin?.GetRefreshRateOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_RefreshPolicyNative") as string
                        ?? "Native");
                OverviewPolicyPills.Children.Add(CreateStatusBadge(refreshLabel, refreshValue, "GlyphBrush", 0.85));
            }

            if (OverviewSessionText != null)
            {
                OverviewSessionText.Text = settings?.Plugin?.GetActiveSessionOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_OverviewSessionIdle") as string ?? "No game session.");
            }

            var statusLabel = TryFindResource("LOCDisplayManager_Status") as string ?? "Status";
            var sessionIdleText = TryFindResource("LOCDisplayManager_OverviewSessionIdle") as string ?? "No game session.";
            var sessionOverview = settings?.Plugin?.GetActiveSessionOverviewText() ?? sessionIdleText;
            var sessionIsIdle = string.Equals(sessionOverview, sessionIdleText, StringComparison.Ordinal);
            if (OverviewSessionStatusText != null)
            {
                var sessionValue = sessionIsIdle
                    ? (TryFindResource("LOCDisplayManager_StatusIdle") as string ?? "Idle")
                    : (TryFindResource("LOCDisplayManager_StatusActive") as string ?? "Active");
                OverviewSessionStatusText.Text = string.Format("{0}: {1}", statusLabel, sessionValue);
                ApplyStatusBadgeAppearance(
                    OverviewSessionStatusText,
                    sessionIsIdle ? "GlyphBrush" : "PositiveRatingBrush",
                    sessionIsIdle ? 0.65 : 1.0);
            }

            if (OverviewNativeHdrText != null)
            {
                OverviewNativeHdrText.Text = settings?.Plugin?.GetNativeHdrOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_OverviewNativeHdrClear") as string
                        ?? "No games have Playnite’s native Enable HDR flag set.");
            }

            var nativeEnabled = settings?.Plugin?.CountNativeHdrEnabledGames() ?? 0;
            if (OverviewNativeHdrStatusText != null)
            {
                var nativeValue = nativeEnabled <= 0
                    ? (TryFindResource("LOCDisplayManager_StatusOk") as string ?? "OK")
                    : (TryFindResource("LOCDisplayManager_StatusConflict") as string ?? "Conflict");
                OverviewNativeHdrStatusText.Text = string.Format("{0}: {1}", statusLabel, nativeValue);
                ApplyStatusBadgeAppearance(
                    OverviewNativeHdrStatusText,
                    nativeEnabled <= 0 ? "PositiveRatingBrush" : "WarningBrush");
            }

            if (OverviewRefreshRateText != null)
            {
                OverviewRefreshRateText.Text = settings?.Plugin?.GetRefreshRateOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_RefreshPolicyNative") as string
                        ?? "Native (do not change refresh rate)");
            }

            SyncHdrMetadataControls();
            SyncNativeHdrMigrationStatus();
            SyncRefreshRateRadios();
        }

        private string GetHdrPolicyBadgeValue(DisplayManagerSettings settings)
        {
            switch (settings?.GlobalHdrPolicy ?? GlobalHdrPolicy.DoNotManage)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    return TryFindResource("LOCDisplayManager_OverviewActionAlwaysOnShort") as string
                        ?? "Always on";
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    return TryFindResource("LOCDisplayManager_OverviewActionMetadataShort") as string
                        ?? "Metadata";
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

        private void SyncAudioSwitcherControls()
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (EnableAudioSwitcherHookCheck != null && settings != null)
            {
                EnableAudioSwitcherHookCheck.IsChecked = settings.EnableAudioSwitcherHook;
            }

            if (AudioSwitcherStatusText != null && plugin != null)
            {
                AudioSwitcherStatusText.Text = plugin.AudioSwitcher?.GetStatusLabel(plugin.Loc)
                    ?? (TryFindResource("LOCDisplayManager_AudioSwitcherMissing") as string
                        ?? "Audio Switcher is not installed.");
            }
        }

        private void EnableAudioSwitcherHookCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || EnableAudioSwitcherHookCheck == null)
            {
                return;
            }

            settings.EnableAudioSwitcherHook = EnableAudioSwitcherHookCheck.IsChecked == true;
        }

        private void SyncRefreshRateRadios()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null || RefreshPolicyNativeRadio == null)
            {
                return;
            }

            var primary = settings.AvailableDisplays?.FirstOrDefault(d => d.IsPrimary && d.IsConnected)
                ?? settings.AvailableDisplays?.FirstOrDefault(d => d.IsConnected);

            if (RefreshRateDetectedText != null)
            {
                if (primary == null)
                {
                    RefreshRateDetectedText.Text = TryFindResource("LOCDisplayManager_RefreshRateDetectedNone") as string
                        ?? "No primary display detected.";
                }
                else
                {
                    var rates = settings.Plugin?.RefreshRates?.GetAvailableRates(primary) ?? Array.Empty<double>();
                    var list = rates.Count == 0
                        ? "—"
                        : string.Join(", ", rates.Select(r => r.ToString("0.###") + " Hz"));
                    var format = TryFindResource("LOCDisplayManager_RefreshRateDetectedFormat") as string
                        ?? "{0}: current {1:0.###} Hz · available at this resolution: {2}";
                    RefreshRateDetectedText.Text = string.Format(
                        format,
                        primary.EffectiveName,
                        primary.RefreshRateHz,
                        list);
                }
            }

            syncingRefreshRadios = true;
            try
            {
                if (RefreshRateExactPanel != null)
                {
                    RefreshRateExactPanel.Children.Clear();
                    var rates = settings.Plugin?.RefreshRates?.GetAvailableRates(primary) ?? Array.Empty<double>();
                    var preferred = settings.PreferredRefreshRateHz;
                    var exactSelected = settings.GlobalRefreshRatePolicy == RefreshRatePolicy.ExactHz
                        || settings.GlobalRefreshRatePolicy == RefreshRatePolicy.Prefer60
                        || settings.GlobalRefreshRatePolicy == RefreshRatePolicy.Prefer120;

                    foreach (var rate in rates)
                    {
                        var format = TryFindResource("LOCDisplayManager_RefreshPolicyExactFormat") as string
                            ?? "{0:0.###} Hz";
                        var radio = new RadioButton
                        {
                            GroupName = "RefreshRatePolicy",
                            Content = string.Format(format, rate),
                            Tag = rate,
                            Margin = new Thickness(0, 8, 0, 0),
                            IsChecked = exactSelected
                                && preferred.HasValue
                                && Math.Abs(preferred.Value - rate) < 0.05
                        };
                        radio.Checked += RefreshRateRadio_OnChecked;
                        RefreshRateExactPanel.Children.Add(radio);
                    }
                }

                switch (settings.GlobalRefreshRatePolicy)
                {
                    case RefreshRatePolicy.HighestDetected:
                        RefreshPolicyHighestRadio.IsChecked = true;
                        break;
                    case RefreshRatePolicy.ExactHz:
                    case RefreshRatePolicy.Prefer60:
                    case RefreshRatePolicy.Prefer120:
                        if (RefreshRateExactPanel == null
                            || !RefreshRateExactPanel.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
                        {
                            RefreshPolicyNativeRadio.IsChecked = true;
                        }
                        break;
                    default:
                        RefreshPolicyNativeRadio.IsChecked = true;
                        break;
                }
            }
            finally
            {
                syncingRefreshRadios = false;
            }
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
                settings.GlobalRefreshRatePolicy = RefreshRatePolicy.HighestDetected;
                settings.PreferredRefreshRateHz = null;
            }
            else if (radio.Tag is double hz)
            {
                settings.GlobalRefreshRatePolicy = RefreshRatePolicy.ExactHz;
                settings.PreferredRefreshRateHz = hz;
            }
            else
            {
                settings.GlobalRefreshRatePolicy = RefreshRatePolicy.Native;
                settings.PreferredRefreshRateHz = null;
            }

            if (OverviewRefreshRateText != null)
            {
                OverviewRefreshRateText.Text = settings.Plugin?.GetRefreshRateOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_RefreshPolicyNative") as string
                        ?? "Native (do not change refresh rate)");
            }
        }

        private void SyncNativeHdrMigrationStatus()
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (NativeHdrMigrationStatusText == null || plugin == null)
            {
                return;
            }

            var enabled = plugin.CountNativeHdrEnabledGames();
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
            var text = new TextBlock
            {
                Text = string.Format(
                    "{0}: {1}",
                    label ?? string.Empty,
                    string.IsNullOrWhiteSpace(value) ? "\u2014" : value)
            };
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
                RebuildGameProfileRows();
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
            RebuildGameProfileRows();
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

            var statusLabel = TryFindResource("LOCDisplayManager_Status") as string ?? "Status";
            var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            if (display.IsConnected)
            {
                pills.Children.Add(CreateStatusBadge(
                    statusLabel,
                    TryFindResource("LOCDisplayManager_StatusConnected") as string ?? "Connected",
                    "PositiveRatingBrush"));
            }
            else
            {
                pills.Children.Add(CreateStatusBadge(
                    statusLabel,
                    TryFindResource("LOCDisplayManager_StatusDisconnected") as string ?? "Disconnected",
                    "GlyphBrush",
                    0.65));
            }

            if (display.IsPrimary)
            {
                pills.Children.Add(CreateStatusBadge(
                    statusLabel,
                    TryFindResource("LOCDisplayManager_StatusPrimary") as string ?? "Primary",
                    "PositiveRatingBrush"));
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

            var idHint = new TextBlock
            {
                Text = display.Id,
                Style = TryFindResource("HintText") as Style,
                Margin = new Thickness(0, 0, 0, 12),
                FontFamily = new FontFamily("Consolas")
            };
            root.Children.Add(idHint);

            var aliasLabel = new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_DisplayAlias") as string ?? "Custom name",
                Style = TryFindResource("FieldLabel") as Style
            };
            root.Children.Add(aliasLabel);

            var aliasBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(display.CustomName) ? (display.Name ?? string.Empty) : display.CustomName,
                MinHeight = 36,
                Margin = new Thickness(0, 0, 0, 4)
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
            };
            root.Children.Add(aliasBox);

            var aliasHelp = new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_DisplayAliasHelp") as string
                    ?? "Optional Playnite name. Disconnected displays with a custom name stay listed.",
                Style = TryFindResource("HintText") as Style,
                Margin = new Thickness(0, 0, 0, 0)
            };
            root.Children.Add(aliasHelp);

            var showCheck = new CheckBox
            {
                Content = TryFindResource("LOCDisplayManager_DisplayShowInList") as string ?? "Show in Display Manager",
                IsChecked = display.IsVisible,
                Margin = new Thickness(0, 12, 0, 0)
            };
            showCheck.Checked += (_, __) => display.IsVisible = true;
            showCheck.Unchecked += (_, __) => display.IsVisible = false;
            root.Children.Add(showCheck);

            card.Child = root;
            return card;
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

            NoGameProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var profile in profiles)
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
                Text = string.IsNullOrWhiteSpace(profile.GameName)
                    ? (TryFindResource("LOCDisplayManager_UnknownGame") as string ?? "Unknown game")
                    : profile.GameName,
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
                    && settings.Plugin.ConfirmRemoveGameProfile(profile.GameName);
                if (!confirmed)
                {
                    return;
                }

                settings.AvailableGameProfiles.Remove(profile);
                RebuildGameProfileRows();
            };
            Grid.SetColumn(removeButton, 1);
            titleRow.Children.Add(removeButton);
            content.Children.Add(titleRow);

            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_GameMenuHdrSection") as string ?? "HDR",
                FormatHdrOverrideSummary(profile.HdrOverride)));
            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_GameMenuHzSection") as string ?? "Refresh rate",
                FormatRefreshOverrideSummary(profile.RefreshRateOverride)));
            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_GameMenuAudioSection") as string ?? "Audio",
                FormatAudioAssociationSummary(profile, settings)));

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
                    return TryFindResource("LOCDisplayManager_GameHdrInherit") as string ?? "Inherit global policy";
            }
        }

        private string FormatRefreshOverrideSummary(GameRefreshRateOverride value)
        {
            switch (value)
            {
                case GameRefreshRateOverride.Native:
                    return TryFindResource("LOCDisplayManager_GameHzNative") as string ?? "Force native";
                case GameRefreshRateOverride.Prefer60:
                    return TryFindResource("LOCDisplayManager_GameHz60") as string ?? "Prefer ~60 Hz";
                case GameRefreshRateOverride.Prefer120:
                    return TryFindResource("LOCDisplayManager_GameHz120") as string ?? "Prefer ~120 Hz";
                case GameRefreshRateOverride.HighestDetected:
                    return TryFindResource("LOCDisplayManager_GameHzHighest") as string ?? "Highest detected";
                default:
                    return TryFindResource("LOCDisplayManager_GameHzInherit") as string ?? "Inherit global refresh policy";
            }
        }

        private string FormatAudioAssociationSummary(GameDisplayProfileEntry profile, DisplayManagerSettings settings)
        {
            if (string.IsNullOrWhiteSpace(profile.AssociatedAudioDeviceId))
            {
                return TryFindResource("LOCDisplayManager_GameAudioNone") as string ?? "No associated playback device";
            }

            try
            {
                var devices = settings?.Plugin?.AudioSwitcher?.GetPlaybackDevices();
                var match = devices?.FirstOrDefault(d =>
                    string.Equals(d.Id, profile.AssociatedAudioDeviceId, StringComparison.OrdinalIgnoreCase));
                if (match != null && !string.IsNullOrWhiteSpace(match.Name))
                {
                    return match.Name;
                }
            }
            catch
            {
                // Soft bridge may throw if Audio Switcher is unavailable mid-call.
            }

            return profile.AssociatedAudioDeviceId;
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
                    return "0.1.0";
                }

                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                return "0.1.0";
            }
        }
    }
}
