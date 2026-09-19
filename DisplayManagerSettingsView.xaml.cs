using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using PlayniteDisplayManager.Displays;

namespace PlayniteDisplayManager
{
    public partial class DisplayManagerSettingsView : UserControl
    {
        private PlayniteDisplayManagerPlugin subscribedPlugin;

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
            UpdateOverview();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
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
                    UpdateOverview();
                }));
                return;
            }

            RebuildDisplayCards();
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
                return;
            }

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
