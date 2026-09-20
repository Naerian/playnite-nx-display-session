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
        private bool syncingDisplayProfileUi;
        private Guid? editingDisplayProfileId;
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

        private DisplayProfile GetEditingDisplayProfile(DisplayManagerSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            settings.MigrateDisplayProfilesPublic();
            if (editingDisplayProfileId.HasValue)
            {
                var match = settings.GetDisplayProfile(editingDisplayProfileId);
                if (match != null)
                {
                    return match;
                }
            }

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
            syncingDisplayProfileUi = true;
            try
            {
                if (DisplayProfileBox != null)
                {
                    var profiles = settings.DisplayProfiles?.ToList() ?? new System.Collections.Generic.List<DisplayProfile>();
                    DisplayProfileBox.ItemsSource = profiles;
                    var selected = editingDisplayProfileId ?? settings.DefaultDisplayProfileId;
                    if (!selected.HasValue || profiles.All(p => p.Id != selected.Value))
                    {
                        selected = settings.DefaultDisplayProfileId ?? profiles.FirstOrDefault()?.Id;
                    }

                    editingDisplayProfileId = selected;
                    DisplayProfileBox.SelectedValue = selected;
                }

                if (DisplayProfileDefaultHint != null)
                {
                    var defaults = settings.GetDefaultDisplayProfile();
                    DisplayProfileDefaultHint.Text = defaults == null
                        ? string.Empty
                        : string.Format(
                            TryFindResource("LOCDisplayManager_DisplayProfileDefaultFormat") as string
                            ?? "Default for launch: {0}",
                            defaults.Name);
                }

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
            }
            finally
            {
                syncingDisplayProfileUi = false;
            }

            RefreshTopologyTargetBox();
            UpdateActiveDisplayProfileCallout();
        }

        private void RefreshTopologyTargetBox()
        {
            if (TopologyTargetBox == null)
            {
                return;
            }

            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingDisplayProfile(settings);
            var previous = profile?.PreferredPlayDisplayId;
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
                    var fallback = profile?.FallbackDisplayId;
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

                if (MissingDisplayPolicyBox != null && profile != null)
                {
                    MissingDisplayPolicyBox.SelectedValue = profile.MissingDisplayPolicy.ToString();
                }

                if (TopologyTurnOffOthersCheck != null && profile != null)
                {
                    TopologyTurnOffOthersCheck.IsChecked = profile.TurnOffOtherDisplays;
                }
            }
            finally
            {
                syncingTopologyTarget = false;
            }

            SyncHdrCapabilityUi();
        }

        private void PersistEditingDisplayProfile(Action<DisplayProfile> mutate)
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingDisplayProfile(settings);
            if (settings == null || profile == null || mutate == null)
            {
                return;
            }

            mutate(profile);
            if (settings.DefaultDisplayProfileId == profile.Id)
            {
                settings.SyncLegacyFieldsFromDefaultTopology();
            }
        }

        private void DisplayProfileBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingDisplayProfileUi)
            {
                return;
            }

            if (DisplayProfileBox?.SelectedValue is Guid id)
            {
                editingDisplayProfileId = id;
                RefreshTopologyTargetBox();
                UpdateActiveDisplayProfileCallout();
                RebuildDisplayCards();
                UpdateOverview();
            }
        }

        private void DisplayProfileNew_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            if (settings == null)
            {
                return;
            }

            var caption = TryFindResource("LOCDisplayManager_DisplayProfilesTitle") as string ?? "display profiles";
            var prompt = TryFindResource("LOCDisplayManager_DisplayProfileNewPrompt") as string ?? "Profile name";
            var suggested = TryFindResource("LOCDisplayManager_DisplayProfileNewDefault") as string ?? "New profile";
            var result = settings.Plugin?.PlayniteApi?.Dialogs?.SelectString(prompt, caption, suggested);
            if (result == null || !result.Result || string.IsNullOrWhiteSpace(result.SelectedString))
            {
                return;
            }

            var created = new DisplayProfile
            {
                Id = Guid.NewGuid(),
                Name = result.SelectedString.Trim(),
                PreferredPlayDisplayId = null,
                TurnOffOtherDisplays = false,
                MissingDisplayPolicy = MissingDisplayPolicy.UseWindowsPrimary
            };
            settings.DisplayProfiles.Add(created);
            editingDisplayProfileId = created.Id;
            RefreshDisplayProfilesUi();
        }

        private void UpdateActiveDisplayProfileCallout()
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingDisplayProfile(settings);
            if (ActiveDisplayProfileCalloutTitle == null || ActiveDisplayProfileCalloutBody == null)
            {
                return;
            }

            ActiveDisplayProfileCalloutTitle.Text = profile?.Name ?? string.Empty;
            var isDefault = settings != null && profile != null && settings.DefaultDisplayProfileId == profile.Id;
            ActiveDisplayProfileCalloutBody.Text = isDefault
                ? (TryFindResource("LOCDisplayManager_ActiveProfileDefaultSource") as string ?? "Launch default")
                : (TryFindResource("LOCDisplayManager_DisplayProfilesTitle") as string ?? "Display profile");
        }

        private void DisplayProfileRename_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingDisplayProfile(settings);
            if (profile == null)
            {
                return;
            }

            var caption = TryFindResource("LOCDisplayManager_DisplayProfilesTitle") as string ?? "display profiles";
            var prompt = TryFindResource("LOCDisplayManager_DisplayProfileRenamePrompt") as string ?? "New name";
            var result = settings.Plugin?.PlayniteApi?.Dialogs?.SelectString(prompt, caption, profile.Name);
            if (result == null || !result.Result || string.IsNullOrWhiteSpace(result.SelectedString))
            {
                return;
            }

            profile.Name = result.SelectedString.Trim();
            RefreshDisplayProfilesUi();
        }

        private void DisplayProfileDelete_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingDisplayProfile(settings);
            if (settings == null || profile == null || settings.DisplayProfiles.Count <= 1)
            {
                MessageBox.Show(
                    TryFindResource("LOCDisplayManager_DisplayProfileDeleteLast") as string
                    ?? "Keep at least one display profile.",
                    "Display Manager");
                return;
            }

            settings.DisplayProfiles.RemoveAll(p => p.Id == profile.Id);
            if (settings.DefaultDisplayProfileId == profile.Id)
            {
                settings.DefaultDisplayProfileId = settings.DisplayProfiles[0].Id;
                settings.SyncLegacyFieldsFromDefaultTopology();
            }

            editingDisplayProfileId = settings.DefaultDisplayProfileId;
            RefreshDisplayProfilesUi();
            RebuildDisplayCards();
            UpdateOverview();
        }

        private void DisplayProfileView_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingDisplayProfile(settings);
            if (settings == null || profile == null)
            {
                return;
            }

            var window = new DisplayProfileViewWindow(settings, profile)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }

        private void DisplayProfileSetDefault_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = DataContext as DisplayManagerSettings;
            var profile = GetEditingDisplayProfile(settings);
            if (settings == null || profile == null)
            {
                return;
            }

            settings.DefaultDisplayProfileId = profile.Id;
            settings.SyncLegacyFieldsFromDefaultTopology();
            RefreshDisplayProfilesUi();
            RebuildDisplayCards();
            UpdateOverview();
        }

        private void TopologyTargetBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingTopologyTarget)
            {
                return;
            }

            var selectedId = TopologyTargetBox?.SelectedValue as string;
            PersistEditingDisplayProfile(profile =>
            {
                if (string.IsNullOrWhiteSpace(selectedId)
                    || string.Equals(selectedId, WindowsPlayDisplayChoiceId, StringComparison.Ordinal))
                {
                    profile.PreferredPlayDisplayId = null;
                }
                else
                {
                    profile.PreferredPlayDisplayId = selectedId;
                }
            });

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

            PersistEditingDisplayProfile(profile =>
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
                PersistEditingDisplayProfile(profile => profile.MissingDisplayPolicy = policy);
            }
        }

        private void MissingDisplayFallbackBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingTopologyTarget)
            {
                return;
            }

            var selectedId = MissingDisplayFallbackBox?.SelectedValue as string;
            PersistEditingDisplayProfile(profile =>
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
                if (ResolutionExactPanel != null)
                {
                    ResolutionExactPanel.Children.Clear();
                    var modes = settings.Plugin?.Resolutions?.GetAvailableModes(primary)
                        ?? (IReadOnlyList<ResolutionMode>)Array.Empty<ResolutionMode>();
                    var preferredW = settings.PreferredResolutionWidth;
                    var preferredH = settings.PreferredResolutionHeight;
                    var exactSelected = settings.GlobalResolutionPolicy == ResolutionPolicy.Exact;

                    foreach (var mode in modes)
                    {
                        var format = TryFindResource("LOCDisplayManager_ResolutionPolicyExactFormat") as string
                            ?? "{0} × {1}";
                        var radio = new RadioButton
                        {
                            GroupName = "ResolutionPolicy",
                            Content = string.Format(format, mode.Width, mode.Height),
                            Tag = mode,
                            Margin = new Thickness(0, 8, 0, 0),
                            IsChecked = exactSelected
                                && preferredW == mode.Width
                                && preferredH == mode.Height
                        };
                        radio.Checked += ResolutionRadio_OnChecked;
                        ResolutionExactPanel.Children.Add(radio);
                    }
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
                        if (ResolutionExactPanel == null
                            || !ResolutionExactPanel.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
                        {
                            ResolutionPolicyNativeRadio.IsChecked = true;
                        }
                        break;
                    default:
                        ResolutionPolicyNativeRadio.IsChecked = true;
                        break;
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

            var modes = settings?.Plugin?.Resolutions?.GetAvailableModes(primary)
                ?? (IReadOnlyList<ResolutionMode>)Array.Empty<ResolutionMode>();
            ResolutionPrimaryPanel.Children.Add(new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_ResolutionAvailableLabel") as string
                    ?? "Available modes",
                Style = TryFindResource("FieldLabel") as Style,
                Margin = new Thickness(0, 4, 0, 4)
            });

            if (modes.Count == 0)
            {
                ResolutionPrimaryPanel.Children.Add(new TextBlock
                {
                    Text = "—",
                    Style = TryFindResource("HintText") as Style
                });
                return;
            }

            var modePills = new WrapPanel();
            foreach (var mode in modes.Take(12))
            {
                modePills.Children.Add(CreateStatusBadge(
                    null,
                    mode.Label,
                    "GlyphBrush",
                    0.95));
            }

            if (modes.Count > 12)
            {
                modePills.Children.Add(CreateStatusBadge(
                    null,
                    "+" + (modes.Count - 12),
                    "GlyphBrush",
                    0.95));
            }

            ResolutionPrimaryPanel.Children.Add(modePills);
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
            else if (radio.Tag is ResolutionMode mode)
            {
                settings.GlobalResolutionPolicy = ResolutionPolicy.Exact;
                settings.PreferredResolutionWidth = mode.Width;
                settings.PreferredResolutionHeight = mode.Height;
            }
            else
            {
                settings.GlobalResolutionPolicy = ResolutionPolicy.Native;
                settings.PreferredResolutionWidth = null;
                settings.PreferredResolutionHeight = null;
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

            if (display.IsConnected)
            {
                var identifyButton = new Button
                {
                    Content = TryFindResource("LOCDisplayManager_IdentifyDisplay") as string ?? "Identify",
                    MinWidth = 120,
                    MinHeight = 36,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 4),
                    Cursor = Cursors.Hand,
                    Style = TryFindResource("NarianPrimaryButton") as Style
                };
                var displayCapture = display;
                identifyButton.Click += (_, __) => IdentifyDisplay(displayCapture);
                root.Children.Add(identifyButton);
                root.Children.Add(new TextBlock
                {
                    Text = TryFindResource("LOCDisplayManager_IdentifyDisplayHint") as string
                        ?? "Shows a large label on that monitor for a few seconds.",
                    Style = TryFindResource("HintText") as Style,
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }

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
                RefreshTopologyTargetBox();
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

        private void IdentifyDisplay(DisplayInfo display)
        {
            var primary = TryFindResource("LOCDisplayManager_StatusPrimary") as string ?? "Primary";
            DisplayIdentifyOverlay.Show(display, primary);
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
                TryFindResource("LOCDisplayManager_GameMenuDisplaySection") as string ?? "Display",
                FormatPlayDisplayOverrideSummary(profile.PreferredPlayDisplayId, settings)));
            content.Children.Add(CreateProfileSummaryLine(
                TryFindResource("LOCDisplayManager_DisplayProfilesTitle") as string ?? "display profile",
                FormatDisplayProfileSummary(profile.DisplayProfileId, settings)));
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

            content.Children.Add(new TextBlock
            {
                Text = TryFindResource("LOCDisplayManager_DisplayProfilesTitle") as string ?? "display profile",
                Style = TryFindResource("FieldLabel") as Style
            });
            var topoBox = new ComboBox
            {
                MinHeight = 36,
                Margin = new Thickness(0, 4, 0, 12),
                DisplayMemberPath = "Name",
                SelectedValuePath = "Id",
                ItemsSource = settings?.DisplayProfiles
            };
            topoBox.SelectedValue = profile.DisplayProfileId;
            topoBox.SelectionChanged += (_, __) =>
            {
                if (topoBox.SelectedValue is Guid id)
                {
                    profile.DisplayProfileId = id;
                }
                else
                {
                    profile.DisplayProfileId = null;
                }
            };
            content.Children.Add(topoBox);

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

        private string FormatDisplayProfileSummary(Guid? DisplayProfileId, DisplayManagerSettings settings)
        {
            if (!DisplayProfileId.HasValue)
            {
                return TryFindResource("LOCDisplayManager_GameDisplayInherit") as string
                    ?? "Keep global settings";
            }

            var match = settings?.GetDisplayProfile(DisplayProfileId);
            return match?.Name ?? DisplayProfileId.Value.ToString();
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
