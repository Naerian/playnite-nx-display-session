using System;
using System.IO;
using Playnite.SDK.Data;

namespace PlayniteDisplayManager
{
    /// <summary>
    /// Remembers the Settings host window size/position/maximized state across opens.
    /// Stored separately from plugin settings so Save/Cancel does not affect it.
    /// </summary>
    public sealed class SettingsWindowPlacement
    {
        public double Width { get; set; }

        public double Height { get; set; }

        public double Left { get; set; }

        public double Top { get; set; }

        public bool Maximized { get; set; }
    }

    public static class SettingsWindowPlacementStore
    {
        private const string FileName = "settings-window.json";
        private const double MinWidth = 1000;
        private const double MinHeight = 700;

        public static SettingsWindowPlacement Load(string userDataPath)
        {
            if (string.IsNullOrWhiteSpace(userDataPath))
            {
                return null;
            }

            var path = Path.Combine(userDataPath, FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                if (!Serialization.TryFromJsonFile<SettingsWindowPlacement>(path, out var loaded) || loaded == null)
                {
                    return null;
                }

                return IsUsable(loaded) ? loaded : null;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string userDataPath, SettingsWindowPlacement placement)
        {
            if (string.IsNullOrWhiteSpace(userDataPath) || !IsUsable(placement))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(userDataPath);
                var path = Path.Combine(userDataPath, FileName);
                File.WriteAllText(path, Serialization.ToJson(placement, true));
            }
            catch
            {
                // Best-effort UI chrome persistence only.
            }
        }

        public static bool IsUsable(SettingsWindowPlacement placement)
        {
            return placement != null
                && !double.IsNaN(placement.Width)
                && !double.IsNaN(placement.Height)
                && !double.IsInfinity(placement.Width)
                && !double.IsInfinity(placement.Height)
                && placement.Width >= MinWidth
                && placement.Height >= MinHeight
                && !double.IsNaN(placement.Left)
                && !double.IsNaN(placement.Top)
                && !double.IsInfinity(placement.Left)
                && !double.IsInfinity(placement.Top);
        }

        public static bool IsOnVirtualScreen(double left, double top, double width, double height)
        {
            try
            {
                var screen = new System.Windows.Rect(
                    System.Windows.SystemParameters.VirtualScreenLeft,
                    System.Windows.SystemParameters.VirtualScreenTop,
                    System.Windows.SystemParameters.VirtualScreenWidth,
                    System.Windows.SystemParameters.VirtualScreenHeight);
                var window = new System.Windows.Rect(left, top, Math.Max(width, 100), Math.Max(height, 100));
                window.Intersect(screen);
                return window.Width >= 80 && window.Height >= 80;
            }
            catch
            {
                return true;
            }
        }
    }
}
