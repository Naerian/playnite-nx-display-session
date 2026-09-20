using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Playnite.SDK;

namespace PlayniteDisplayManager.Restore
{
    /// <summary>
    /// Best-effort: move Playnite's main window onto the current Windows primary monitor.
    /// </summary>
    public static class PlayniteWindowRelocator
    {
        private const uint SwpNosize = 0x0001;
        private const uint SwpNozorder = 0x0004;
        private const uint SwpShowwindow = 0x0040;
        private const int MonitorDefaultToPrimary = 1;

        public static bool TryRelocateToPrimaryMonitor(ILogger log)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var handle = FindMainWindow(process);
                if (handle == IntPtr.Zero)
                {
                    log?.Info("Fullscreen relocate skipped: no Playnite main window.");
                    return false;
                }

                var monitor = MonitorFromWindow(handle, MonitorDefaultToPrimary);
                if (monitor == IntPtr.Zero)
                {
                    monitor = MonitorFromWindow(IntPtr.Zero, MonitorDefaultToPrimary);
                }

                if (monitor == IntPtr.Zero)
                {
                    log?.Warn("Fullscreen relocate failed: no primary monitor.");
                    return false;
                }

                var info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(monitor, ref info))
                {
                    log?.Warn("Fullscreen relocate failed: GetMonitorInfo.");
                    return false;
                }

                var work = info.rcWork;
                var x = work.Left;
                var y = work.Top;
                var width = Math.Max(1, work.Right - work.Left);
                var height = Math.Max(1, work.Bottom - work.Top);

                if (!SetWindowPos(handle, IntPtr.Zero, x, y, width, height,
                        SwpNozorder | SwpShowwindow))
                {
                    // Retry position-only if resize is rejected (some fullscreen styles).
                    if (!SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
                            SwpNosize | SwpNozorder | SwpShowwindow))
                    {
                        log?.Warn("Fullscreen relocate SetWindowPos failed.");
                        return false;
                    }
                }

                log?.Info("Playnite window relocated to primary monitor work area.");
                return true;
            }
            catch (Exception ex)
            {
                log?.Error(ex, "Fullscreen relocate failed.");
                return false;
            }
        }

        private static IntPtr FindMainWindow(Process process)
        {
            if (process == null)
            {
                return IntPtr.Zero;
            }

            var candidates = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid != (uint)process.Id)
                {
                    return true;
                }

                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                var length = GetWindowTextLength(hWnd);
                if (length <= 0)
                {
                    return true;
                }

                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                candidates.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            if (candidates.Count == 0)
            {
                return process.MainWindowHandle;
            }

            // Prefer a window whose title mentions Playnite.
            var playnite = candidates.FirstOrDefault(h =>
            {
                var len = GetWindowTextLength(h);
                var sb = new StringBuilder(len + 1);
                GetWindowText(h, sb, sb.Capacity);
                return sb.ToString().IndexOf("Playnite", StringComparison.OrdinalIgnoreCase) >= 0;
            });

            return playnite != IntPtr.Zero ? playnite : candidates[0];
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
