using System;
using System.Collections.Generic;
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
using PlayniteDisplayManager.Resolution;

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
        private bool syncingResolutionRadios;
        private bool syncingTopologyTarget;
        private const string WindowsPlayDisplayChoiceId = "__windows_primary__";

        private sealed class PlayDisplayChoice
        {
            public string Id { get; set; }
            public string EffectiveName { get; set; }
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

            DataContextChanged += (_, __) =>
            {
                SubscribeDisplaysChanged();
                ApplyAppearancePreset();
                BuildAppearancePresetChips();
                RebuildDisplayCards();
                RebuildGameProfileRows();
                RebuildPlatformProfileRows();
                RefreshDisplayProfilesUi();
                SyncHdrPolicyRadios();
                SyncHdrMetadataControls();
                SyncRefreshRateRadios();
                SyncResolutionRadios();
                SyncDesktopAccessControls();
                SyncFullscreenRelocateControl();
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
            RebuildPlatformProfileRows();
            RefreshDisplayProfilesUi();
            SyncHdrPolicyRadios();
            SyncHdrMetadataControls();
            SyncRefreshRateRadios();
            SyncResolutionRadios();
            SyncDesktopAccessControls();
            SyncFullscreenRelocateControl();
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
                    RefreshDisplayProfilesUi();
                    SyncRefreshRateRadios();
                    SyncResolutionRadios();
                    UpdateOverview();
                }));
                return;
            }

            RebuildDisplayCards();
            RefreshDisplayProfilesUi();
            SyncRefreshRateRadios();
            SyncResolutionRadios();
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
                RebuildGameProfileRows();
                RebuildPlatformProfileRows();
            }), DispatcherPriority.Loaded);
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
            RefreshDisplayProfilesUi();
            UpdateOverview();
        }

        private DisplayProfile GetDefaultDisplayProfileForMissing(DisplayManagerSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            settings.MigrateDisplayProfilesPublic();
            return settings.GetDefaultDisplayProfile();
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

            RefreshTopologyTargetBox();
        }

        private void RefreshTopologyTargetBox()
        {
            if (TopologyTargetBox == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            var previous = settings?.PreferredPlayDisplayId;
            var defaults = GetDefaultDisplayProfileForMissing(settings);
            var connected = settings?.AvailableDisplays?
                .Where(d => d.IsConnected)
                .ToList() ?? new System.Collections.Generic.List<DisplayInfo>();

            var choices = new System.Collections.Generic.List<PlayDisplayChoice>
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

            syncingTopologyTarget = true;
            try
            {
                TopologyTargetBox.ItemsSource = choices;
                if (string.IsNullOrWhiteSpace(previous))
                {
                    TopologyTargetBox.SelectedValue = WindowsPlayDisplayChoiceId;
                }
                else if (connected.Any(d => string.Equals(d.Id, previous, StringComparison.OrdinalIgnoreCase)))
                {
                    TopologyTargetBox.SelectedValue = previous;
                }
                else
                {
                    TopologyTargetBox.SelectedValue = WindowsPlayDisplayChoiceId;
                }

                if (MissingDisplayFallbackBox != null)
                {
                    MissingDisplayFallbackBox.ItemsSource = choices;
                    var fallback = defaults?.FallbackDisplayId;
                    if (!string.IsNullOrWhiteSpace(fallback)
                        && connected.Any(d => string.Equals(d.Id, fallback, StringComparison.OrdinalIgnoreCase)))
                    {
                        MissingDisplayFallbackBox.SelectedValue = fallback;
                    }
                    else
                    {
                        MissingDisplayFallbackBox.SelectedValue = WindowsPlayDisplayChoiceId;
                    }
                }

                if (MissingDisplayPolicyBox != null && defaults != null)
                {
                    MissingDisplayPolicyBox.SelectedValue = defaults.MissingDisplayPolicy.ToString();
                }

                if (TopologyTurnOffOthersCheck != null)
                {
                    TopologyTurnOffOthersCheck.IsChecked = settings?.TurnOffOtherDisplaysOnLaunch == true;
                }
            }
            finally
            {
                syncingTopologyTarget = false;
            }

            SyncHdrCapabilityUi();
        }

        private void PersistLaunchDisplaySettings()
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            var defaults = settings.GetDefaultDisplayProfile();
            if (defaults != null)
            {
                defaults.PreferredPlayDisplayId = settings.PreferredPlayDisplayId;
                defaults.TurnOffOtherDisplays = settings.TurnOffOtherDisplaysOnLaunch;
            }
        }

        private void PersistDefaultDisplayProfile(Action<DisplayProfile> mutate)
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetDefaultDisplayProfileForMissing(settings);
            if (settings == null || profile == null || mutate == null)
            {
                return;
            }

            mutate(profile);
            settings.SyncLegacyFieldsFromDefaultTopology();
        }

        private void TopologyTargetBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingTopologyTarget)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            var selectedId = TopologyTargetBox?.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(selectedId)
                || string.Equals(selectedId, WindowsPlayDisplayChoiceId, StringComparison.Ordinal))
            {
                settings.PreferredPlayDisplayId = null;
            }
            else
            {
                settings.PreferredPlayDisplayId = selectedId;
            }

            PersistLaunchDisplaySettings();
            RebuildDisplayCards();
            SyncHdrCapabilityUi();
            UpdateOverview();
        }

        private void TopologyTurnOffOthersCheck_OnChanged(object sender, RoutedEventArgs e)
        {
            if (syncingTopologyTarget || TopologyTurnOffOthersCheck == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            settings.TurnOffOtherDisplaysOnLaunch = TopologyTurnOffOthersCheck.IsChecked == true;
            PersistLaunchDisplaySettings();
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
            PersistDefaultDisplayProfile(profile =>
            {
                if (string.IsNullOrWhiteSpace(selectedId)
                    || string.Equals(selectedId, WindowsPlayDisplayChoiceId, StringComparison.Ordinal))
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

        private void TopologyTrialApply_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var plugin = settings?.Plugin;
            if (plugin == null)
            {
                return;
            }

            var targetId = TopologyTargetBox?.SelectedValue as string;
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

            var turnOffOthers = settings.TurnOffOtherDisplaysOnLaunch
                || TopologyTurnOffOthersCheck?.IsChecked == true;
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
            var connected = displays?.Where(d => d.IsConnected).ToList()
                ?? new System.Collections.Generic.List<DisplayInfo>();
            var primary = connected.FirstOrDefault(d => d.IsPrimary) ?? connected.FirstOrDefault();
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
                if (primary != null)
                {
                    OverviewDisplayPills.Children.Add(CreateStatusBadge(
                        null,
                        primary.EffectiveName,
                        "PositiveRatingBrush"));
                    OverviewDisplayPills.Children.Add(CreateStatusBadge(
                        null,
                        TryFindResource("LOCDisplayManager_StatusPrimary") as string ?? "Primary",
                        "PositiveRatingBrush"));
                    if (primary.Width > 0 && primary.Height > 0)
                    {
                        OverviewDisplayPills.Children.Add(CreateStatusBadge(
                            null,
                            primary.Width + "×" + primary.Height,
                            "GlyphBrush",
                            0.95));
                    }

                    if (primary.RefreshRateHz > 0)
                    {
                        OverviewDisplayPills.Children.Add(CreateStatusBadge(
                            null,
                            primary.RefreshRateHz.ToString("0.###") + " Hz",
                            "GlyphBrush",
                            0.95));
                    }

                    var primaryHdr = ProbeDisplayHdrSupported(settings, primary);
                    OverviewDisplayPills.Children.Add(CreateStatusBadge(
                        null,
                        primaryHdr
                            ? (TryFindResource("LOCDisplayManager_StatusHdrSupported") as string ?? "HDR supported")
                            : (TryFindResource("LOCDisplayManager_StatusHdrNotSupported") as string ?? "No HDR"),
                        primaryHdr ? "PositiveRatingBrush" : "GlyphBrush",
                        primaryHdr ? 1.0 : 0.9));
                }
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
                    var managingHdr = settings?.GlobalHdrPolicy != GlobalHdrPolicy.DoNotManage;
                    OverviewPolicyPills.Children.Add(CreateStatusBadge(
                        null,
                        hdrValue,
                        managingHdr ? "PositiveRatingBrush" : "GlyphBrush",
                        managingHdr ? 1.0 : 0.9));
                }
            }

            if (OverviewNativeHdrText != null)
            {
                OverviewNativeHdrText.Text = settings?.Plugin?.GetNativeHdrOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_OverviewNativeHdrClear") as string
                        ?? "No games have Playnite's Enable HDR option turned on.");
            }

            var nativeEnabled = settings?.Plugin?.CountNativeHdrEnabledGames() ?? 0;
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

            SyncHdrMetadataControls();
            SyncNativeHdrMigrationStatus();
            SyncRefreshRateRadios();
            SyncResolutionRadios();
            SyncHdrCapabilityUi();
        }

        private static DisplayInfo ResolvePreferredPlayDisplay(
            DisplayManagerSettings settings,
            System.Collections.Generic.IList<DisplayInfo> connected)
        {
            if (connected == null || connected.Count == 0)
            {
                return null;
            }

            var preferredId = settings?.PreferredPlayDisplayId;
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

        private static bool ProbeDisplayHdrSupported(DisplayManagerSettings settings, DisplayInfo display)
        {
            if (display == null || settings?.Plugin?.Hdr == null)
            {
                return false;
            }

            try
            {
                var probe = settings.Plugin.Hdr.ProbeActiveTargets(new[] { display }).FirstOrDefault();
                return probe != null && probe.HdrSupported;
            }
            catch
            {
                return false;
            }
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

            var rates = settings?.Plugin?.RefreshRates?.GetAvailableRates(primary) ?? Array.Empty<double>();
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

        private static bool IsConfiguredPlayPrimary(DisplayInfo display, DisplayManagerSettings settings)
        {
            if (display == null || settings == null)
            {
                return false;
            }

            var defaults = settings.GetDefaultDisplayProfile();
            var preferredId = defaults?.PreferredPlayDisplayId ?? settings.PreferredPlayDisplayId;
            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                return string.Equals(display.Id, preferredId, StringComparison.OrdinalIgnoreCase);
            }

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
                var modes = settings.Plugin?.Resolutions?.GetAvailableModes(primary)
                    ?? (IReadOnlyList<ResolutionMode>)Array.Empty<ResolutionMode>();
                if (ResolutionExactBox != null)
                {
                    ResolutionExactBox.ItemsSource = modes.ToList();
                    ResolutionExactBox.IsEnabled = modes.Count > 0;

                    ResolutionMode selected = null;
                    if (settings.PreferredResolutionWidth > 0 && settings.PreferredResolutionHeight > 0)
                    {
                        selected = modes.FirstOrDefault(m =>
                            m.Width == settings.PreferredResolutionWidth
                            && m.Height == settings.PreferredResolutionHeight);
                    }

                    if (selected == null && settings.GlobalResolutionPolicy == ResolutionPolicy.Exact)
                    {
                        selected = modes.FirstOrDefault();
                    }

                    ResolutionExactBox.SelectedItem = selected;
                }

                switch (settings.GlobalResolutionPolicy)
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
                    ResolutionExactBox.IsEnabled =
                        ResolutionPolicyExactRadio?.IsChecked == true && modes.Count > 0;
                }
            }
            finally
            {
                syncingResolutionRadios = false;
            }
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

            var modeCount = settings?.Plugin?.Resolutions?.GetAvailableModes(primary)?.Count ?? 0;
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
                settings.GlobalResolutionPolicy = ResolutionPolicy.LowestAvailable;
                settings.PreferredResolutionWidth = null;
                settings.PreferredResolutionHeight = null;
            }
            else if (ReferenceEquals(radio, ResolutionPolicyHighestRadio)
                || string.Equals(radio.Tag as string, "HighestAvailable", StringComparison.OrdinalIgnoreCase))
            {
                settings.GlobalResolutionPolicy = ResolutionPolicy.HighestAvailable;
                settings.PreferredResolutionWidth = null;
                settings.PreferredResolutionHeight = null;
            }
            else if (ReferenceEquals(radio, ResolutionPolicyExactRadio)
                || string.Equals(radio.Tag as string, "Exact", StringComparison.OrdinalIgnoreCase))
            {
                settings.GlobalResolutionPolicy = ResolutionPolicy.Exact;
                ApplySelectedExactResolution(settings);
            }
            else
            {
                settings.GlobalResolutionPolicy = ResolutionPolicy.Native;
                settings.PreferredResolutionWidth = null;
                settings.PreferredResolutionHeight = null;
            }

            if (ResolutionExactBox != null)
            {
                ResolutionExactBox.IsEnabled = settings.GlobalResolutionPolicy == ResolutionPolicy.Exact
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
                && settings.GlobalResolutionPolicy != ResolutionPolicy.Exact)
            {
                return;
            }

            settings.GlobalResolutionPolicy = ResolutionPolicy.Exact;
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

            var mode = ResolutionExactBox?.SelectedItem as ResolutionMode;
            if (mode == null && ResolutionExactBox?.Items.Count > 0)
            {
                syncingResolutionRadios = true;
                try
                {
                    ResolutionExactBox.SelectedIndex = 0;
                    mode = ResolutionExactBox.SelectedItem as ResolutionMode;
                }
                finally
                {
                    syncingResolutionRadios = false;
                }
            }

            if (mode != null)
            {
                settings.PreferredResolutionWidth = mode.Width;
                settings.PreferredResolutionHeight = mode.Height;
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

            if (IsConfiguredPlayPrimary(display, DataContext as DisplayManagerSettings))
            {
                pills.Children.Add(CreateStatusBadge(
                    null,
                    TryFindResource("LOCDisplayManager_StatusPrimary") as string ?? "Primary",
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
                RefreshTopologyTargetBox();
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
                });
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
            var rates = settings?.Plugin?.RefreshRates?.GetAvailableRates(primary) ?? Array.Empty<double>();
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
            var modes = settings?.Plugin?.Resolutions?.GetAvailableModes(primary)
                ?? new List<ResolutionMode>();
            foreach (var mode in modes)
            {
                choices.Add(new GameProfileChoice
                {
                    Label = mode.Label,
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
