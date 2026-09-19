using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace PlayniteDisplayManager
{
    public partial class DisplayManagerSettingsView : UserControl
    {
        public DisplayManagerSettingsView()
        {
            InitializeComponent();
            AboutVersionText.Text = string.Format(
                TryFindResource("LOCDisplayManager_VersionAuthorFormat") as string ?? "Display Manager {0} · Narian",
                GetInstalledVersion());

            DataContextChanged += (_, __) =>
            {
                ApplyAppearancePreset();
                BuildAppearancePresetChips();
                UpdateOverview();
            };
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            ApplyAppearancePreset();
            BuildAppearancePresetChips();
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
            UpdateOverview();
        }

        private void UpdateOverview()
        {
            // Live display/HDR enumeration arrives in later steps.
            // Keep Overview honest: unknown / pending until Windows APIs are wired.
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
