using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PlayniteDisplayManager.Displays;

namespace PlayniteDisplayManager.Refresh
{
    public enum RefreshRatePolicy
    {
        /// <summary>Leave the current refresh rate alone.</summary>
        Native = 0,

        /// <summary>Prefer ~60 Hz at the current resolution.</summary>
        Prefer60 = 1,

        /// <summary>Prefer ~120 Hz at the current resolution.</summary>
        Prefer120 = 2,

        /// <summary>Highest rate EnumDisplaySettings reports for the current resolution.</summary>
        HighestDetected = 3
    }

    public enum GameRefreshRateOverride
    {
        Inherit = 0,
        Native = 1,
        Prefer60 = 2,
        Prefer120 = 3,
        HighestDetected = 4
    }

    public sealed class RefreshRatePlan
    {
        public bool ShouldApply { get; set; }

        public double? TargetHz { get; set; }

        public RefreshRatePolicy EffectivePolicy { get; set; }

        public string Reason { get; set; }
    }

    /// <summary>
    /// Optional refresh-rate changes via ChangeDisplaySettingsEx (same resolution).
    /// Restore is owned by the topology snapshot (CCD), not by guessing Hz.
    /// </summary>
    public sealed class RefreshRateService
    {
        private const int EnumCurrentSettings = -1;
        private const int CdsTest = 0x00000002;
        private const uint DmDisplayFrequency = 0x00400000;

        public IReadOnlyList<double> GetAvailableRates(DisplayInfo display)
        {
            var rates = new SortedSet<double>();
            if (display == null || string.IsNullOrWhiteSpace(display.GdiDeviceName))
            {
                return rates.ToList();
            }

            var device = display.GdiDeviceName;
            var width = display.Width;
            var height = display.Height;
            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));

            for (var i = 0; EnumDisplaySettings(device, i, ref mode); i++)
            {
                if (width > 0 && height > 0 &&
                    (mode.dmPelsWidth != width || mode.dmPelsHeight != height))
                {
                    continue;
                }

                if (mode.dmDisplayFrequency > 1)
                {
                    rates.Add(mode.dmDisplayFrequency);
                }
            }

            return rates.ToList();
        }

        public RefreshRatePlan Plan(
            RefreshRatePolicy globalPolicy,
            GameRefreshRateOverride gameOverride,
            DisplayInfo primary)
        {
            var policy = ResolvePolicy(globalPolicy, gameOverride);
            if (policy == RefreshRatePolicy.Native)
            {
                return new RefreshRatePlan
                {
                    ShouldApply = false,
                    EffectivePolicy = RefreshRatePolicy.Native,
                    Reason = gameOverride == GameRefreshRateOverride.Inherit
                        ? "native (global)"
                        : "native (per-game)"
                };
            }

            if (primary == null || !primary.IsConnected)
            {
                return new RefreshRatePlan
                {
                    ShouldApply = false,
                    EffectivePolicy = policy,
                    Reason = "no primary display"
                };
            }

            var available = GetAvailableRates(primary);
            double? target = null;
            switch (policy)
            {
                case RefreshRatePolicy.Prefer60:
                    target = FindClosest(available, 60);
                    break;
                case RefreshRatePolicy.Prefer120:
                    target = FindClosest(available, 120);
                    break;
                case RefreshRatePolicy.HighestDetected:
                    target = available.Count > 0 ? available.Max() : (double?)null;
                    break;
            }

            if (!target.HasValue)
            {
                return new RefreshRatePlan
                {
                    ShouldApply = false,
                    EffectivePolicy = policy,
                    Reason = "requested rate not available at current resolution"
                };
            }

            if (primary.RefreshRateHz > 0 &&
                Math.Abs(primary.RefreshRateHz - target.Value) < 0.6)
            {
                return new RefreshRatePlan
                {
                    ShouldApply = false,
                    TargetHz = target,
                    EffectivePolicy = policy,
                    Reason = "already at " + target.Value.ToString("0.###") + " Hz"
                };
            }

            return new RefreshRatePlan
            {
                ShouldApply = true,
                TargetHz = target,
                EffectivePolicy = policy,
                Reason = "apply " + target.Value.ToString("0.###") + " Hz"
            };
        }

        public bool TryApply(DisplayInfo display, double targetHz, out string error)
        {
            error = null;
            if (display == null || string.IsNullOrWhiteSpace(display.GdiDeviceName))
            {
                error = "Display has no GDI device name.";
                return false;
            }

            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(display.GdiDeviceName, EnumCurrentSettings, ref mode))
            {
                error = "EnumDisplaySettings failed for " + display.GdiDeviceName + ".";
                return false;
            }

            var frequency = (uint)Math.Round(targetHz);
            if (frequency < 1)
            {
                error = "Invalid refresh rate.";
                return false;
            }

            mode.dmDisplayFrequency = frequency;
            mode.dmFields = DmDisplayFrequency;

            var test = ChangeDisplaySettingsEx(display.GdiDeviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
            if (test != 0)
            {
                error = "Refresh rate " + frequency + " Hz is not valid for the current mode (CDS code " + test + ").";
                return false;
            }

            // Temporary mode for this session — topology snapshot restore brings the prior rate back.
            var apply = ChangeDisplaySettingsEx(display.GdiDeviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
            if (apply != 0)
            {
                error = "ChangeDisplaySettingsEx failed with code " + apply + ".";
                return false;
            }

            return true;
        }

        public static RefreshRatePolicy ResolvePolicy(
            RefreshRatePolicy globalPolicy,
            GameRefreshRateOverride gameOverride)
        {
            switch (gameOverride)
            {
                case GameRefreshRateOverride.Native:
                    return RefreshRatePolicy.Native;
                case GameRefreshRateOverride.Prefer60:
                    return RefreshRatePolicy.Prefer60;
                case GameRefreshRateOverride.Prefer120:
                    return RefreshRatePolicy.Prefer120;
                case GameRefreshRateOverride.HighestDetected:
                    return RefreshRatePolicy.HighestDetected;
                default:
                    return globalPolicy;
            }
        }

        private static double? FindClosest(IReadOnlyList<double> available, double desired)
        {
            if (available == null || available.Count == 0)
            {
                return null;
            }

            double? best = null;
            var bestDelta = double.MaxValue;
            foreach (var rate in available)
            {
                var delta = Math.Abs(rate - desired);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = rate;
                }
            }

            // Accept 59.94 for 60, 119.88 for 120, etc.
            if (best.HasValue && bestDelta <= 1.5)
            {
                return best;
            }

            return null;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(
            string lpszDeviceName,
            ref DEVMODE lpDevMode,
            IntPtr hwnd,
            int dwflags,
            IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            private const int CchDeviceName = 32;
            private const int CchFormName = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }
    }
}
