using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Profiles;

namespace PlayniteDisplayManager
{
    public sealed class DisplayProfileViewWindow : Window
    {
        public DisplayProfileViewWindow(DisplayManagerSettings settings, DisplayProfile profile)
        {
            Title = settings?.Plugin?.Loc("LOCDisplayManager_DisplayProfileViewTitle") ?? "Display profile";
            Width = 520;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = BuildContent(settings, profile);
        }

        private static UIElement BuildContent(DisplayManagerSettings settings, DisplayProfile profile)
        {
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = profile?.Name ?? "-",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var primary = ResolveDisplay(settings, profile?.PreferredPlayDisplayId);
            panel.Children.Add(Line("Primary", primary?.EffectiveName ?? "Windows primary"));
            panel.Children.Add(Line("Turn off others", profile?.TurnOffOtherDisplays == true ? "Yes" : "No"));
            panel.Children.Add(Line("Missing display", profile?.MissingDisplayPolicy.ToString() ?? "-"));
            panel.Children.Add(Line("HDR override", profile?.HdrOverride.ToString() ?? "-"));
            panel.Children.Add(Line("Refresh override", profile?.RefreshRateOverride.ToString() ?? "-"));
            panel.Children.Add(Line("Resolution override", profile?.ResolutionOverride.ToString() ?? "-"));
            return panel;
        }

        private static DisplayInfo ResolveDisplay(DisplayManagerSettings settings, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return settings?.AvailableDisplays?.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static UIElement Line(string label, string value)
        {
            return new TextBlock
            {
                Text = label + ": " + (string.IsNullOrWhiteSpace(value) ? "-" : value),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }
    }
}
