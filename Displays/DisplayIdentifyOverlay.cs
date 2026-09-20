using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PlayniteDisplayManager.Displays;

namespace PlayniteDisplayManager.Displays
{
    internal static class DisplayIdentifyOverlay
    {
        public static void Show(DisplayInfo display, string primaryLabel = null)
        {
            if (display == null || !display.IsConnected)
            {
                return;
            }

            try
            {
                System.Windows.Forms.Screen screen = null;
                if (!string.IsNullOrWhiteSpace(display.GdiDeviceName))
                {
                    screen = System.Windows.Forms.Screen.AllScreens
                        .FirstOrDefault(s => string.Equals(s.DeviceName, display.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
                }

                if (screen == null && display.IsPrimary)
                {
                    screen = System.Windows.Forms.Screen.PrimaryScreen;
                }

                if (screen == null)
                {
                    screen = System.Windows.Forms.Screen.AllScreens
                        .FirstOrDefault(s => s.Bounds.Width == display.Width && s.Bounds.Height == display.Height)
                        ?? System.Windows.Forms.Screen.PrimaryScreen;
                }

                if (screen == null)
                {
                    return;
                }

                var label = display.EffectiveName;
                if (display.IsPrimary)
                {
                    var primary = string.IsNullOrWhiteSpace(primaryLabel) ? "Primary" : primaryLabel;
                    label = label + " (" + primary + ")";
                }

                var overlay = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(210, 10, 14, 20)),
                    Topmost = true,
                    ShowInTaskbar = false,
                    ResizeMode = ResizeMode.NoResize,
                    Left = screen.Bounds.Left,
                    Top = screen.Bounds.Top,
                    Width = screen.Bounds.Width,
                    Height = screen.Bounds.Height,
                    Content = new TextBlock
                    {
                        Text = label,
                        FontSize = Math.Max(36, Math.Min(screen.Bounds.Width, screen.Bounds.Height) / 12.0),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(48)
                    }
                };

                overlay.Show();
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                closeTimer.Tick += (_, __) =>
                {
                    closeTimer.Stop();
                    try { overlay.Close(); } catch { }
                };
                closeTimer.Start();
            }
            catch
            {
                // Identify must never break the host UI.
            }
        }
    }
}
