using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;

namespace PlayniteDisplayManager
{
    public partial class DisplayManagerSettingsView : UserControl
    {
        private PlayniteDisplayManagerPlugin subscribedPlugin;
        private DisplaySnapshot topologyTrialSnapshot;
        private DispatcherTimer topologyTrialTimer;
        private int topologyTrialSecondsLeft;

        public DisplayManagerSettingsView()
        {
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
                RefreshTopologyTargetBox();
                SyncHdrPolicyRadios();
                SyncHdrMetadataControls();
                UpdateOverview();
            };
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            ApplyAppearancePreset();
            BuildAppearancePresetChips();
            RebuildDisplayCards();
            RefreshTopologyTargetBox();
            SyncHdrPolicyRadios();
            SyncHdrMetadataControls();
            UpdateOverview();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            StopTopologyTrialTimer(restore: true);
            UnsubscribeDisplaysChanged();
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
            RefreshAppearancePresetChips();
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
                OverviewPolicyText.Text = settings?.Plugin?.GetHdrPolicyOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_OverviewPolicyUnset") as string ?? "Not configured yet.");
            }

            if (OverviewHdrText != null)
            {
                OverviewHdrText.Text = TryFindResource("LOCDisplayManager_OverviewHdrUnknown") as string
                    ?? "Unknown — Display Manager does not trust readback under Automatic Color Management.";
            }

            if (OverviewGameHdrText != null)
            {
                OverviewGameHdrText.Text = settings?.Plugin?.GetSelectedGameHdrOverviewText()
                    ?? (TryFindResource("LOCDisplayManager_OverviewGameHdrNone") as string
                        ?? "Select a game in the library to preview HDR metadata.");
            }

            SyncHdrMetadataControls();

            if (OverviewHdrBadge != null)
            {
                OverviewHdrBadge.Text = TryFindResource("LOCDisplayManager_StatusUnknown") as string ?? "Unknown";
            }
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
                Style = TryFindResource("SummaryPill") as Style,
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

            foreach (var display in displays)
            {
                DisplayCardsPanel.Children.Add(CreateDisplayCard(display));
            }
        }

        private UIElement CreateDisplayCard(DisplayInfo display)
        {
            var card = new Border
            {
                Style = TryFindResource("SummaryCard") as Style,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var root = new StackPanel();
            var title = new TextBlock
            {
                Text = display.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(title);

            var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            pills.Children.Add(CreatePill(display.IsConnected
                ? (TryFindResource("LOCDisplayManager_StatusConnected") as string ?? "Connected")
                : (TryFindResource("LOCDisplayManager_StatusDisconnected") as string ?? "Disconnected")));
            if (display.IsPrimary)
            {
                pills.Children.Add(CreatePill(TryFindResource("LOCDisplayManager_StatusPrimary") as string ?? "Primary"));
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
                Text = display.CustomName ?? string.Empty,
                MinHeight = 36,
                Margin = new Thickness(0, 0, 0, 4)
            };
            aliasBox.TextChanged += (_, __) =>
            {
                display.CustomName = aliasBox.Text;
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
