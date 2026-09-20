using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PlayniteDisplayManager.Displays;

namespace PlayniteDisplayManager.Resolution
{
    public enum ResolutionPolicy
    {
        /// <summary>Leave current resolution alone.</summary>
        Native = 0,

        /// <summary>Use PreferredWidth x PreferredHeight when available.</summary>
        Exact = 1,

        /// <summary>Lowest area mode EnumDisplaySettings reports for the target.</summary>
        LowestAvailable = 2,

        /// <summary>Highest area mode EnumDisplaySettings reports for the target.</summary>
        HighestAvailable = 3
    }

    public enum GameResolutionOverride
    {
        Inherit = 0,
        Native = 1,
        Exact = 2,
        LowestAvailable = 3,
        HighestAvailable = 4
    }

    public sealed class ResolutionMode
    {
        public int Width { get; set; }

        public int Height { get; set; }

        public string Label => Width + " × " + Height;
    }

    public sealed class ResolutionPlan
    {
        public bool ShouldApply { get; set; }

        public int? TargetWidth { get; set; }

        public int? TargetHeight { get; set; }

        public ResolutionPolicy EffectivePolicy { get; set; }

        public string Reason { get; set; }
    }

    /// <summary>
    /// Optional resolution changes via ChangeDisplaySettingsEx.
    /// Restore is owned by the display snapshot (CCD), not by guessing modes.
    /// </summary>
    public sealed class ResolutionService
    {
        private const int EnumCurrentSettings = -1;
        private const int CdsTest = 0x00000002;
        private const int CdsUpdateregistry = 0x00000001;
        private const uint DmPelsWidth = 0x00080000;
        private const uint DmPelsHeight = 0x00100000;
        private const uint DmDisplayFrequency = 0x00400000;

        public IReadOnlyList<ResolutionMode> GetAvailableModes(DisplayInfo display)
        {
            var modes = new SortedDictionary<long, ResolutionMode>();
            if (display == null || string.IsNullOrWhiteSpace(display.GdiDeviceName))
            {
                return new List<ResolutionMode>();
            }

            var device = display.GdiDeviceName;
            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));

            for (var i = 0; EnumDisplaySettings(device, i, ref mode); i++)
            {
                if (mode.dmPelsWidth < 640 || mode.dmPelsHeight < 480)
                {
                    continue;
                }

                var key = ((long)mode.dmPelsWidth << 32) | (uint)mode.dmPelsHeight;
                if (!modes.ContainsKey(key))
                {
                    modes[key] = new ResolutionMode
                    {
                        Width = mode.dmPelsWidth,
                        Height = mode.dmPelsHeight
                    };
                }
            }

            return modes.Values
                .OrderByDescending(m => (long)m.Width * m.Height)
                .ToList();
        }

        public ResolutionPlan Plan(
            ResolutionPolicy globalPolicy,
            GameResolutionOverride gameOverride,
            DisplayInfo target,
            int? preferredWidth = null,
            int? preferredHeight = null)
        {
            var policy = ResolvePolicy(globalPolicy, gameOverride);
            if (policy == ResolutionPolicy.Native)
            {
                return new ResolutionPlan
                {
                    ShouldApply = false,
                    EffectivePolicy = ResolutionPolicy.Native,
                    Reason = gameOverride == GameResolutionOverride.Inherit
                        ? "native (global)"
                        : "native (override)"
                };
            }

            if (target == null || string.IsNullOrWhiteSpace(target.GdiDeviceName))
            {
                return new ResolutionPlan
                {
                    ShouldApply = false,
                    EffectivePolicy = policy,
                    Reason = "no target display"
                };
            }

            var available = GetAvailableModes(target);
            if (available.Count == 0)
            {
                return new ResolutionPlan
                {
                    ShouldApply = false,
                    EffectivePolicy = policy,
                    Reason = "no modes"
                };
            }

            ResolutionMode chosen = null;
            switch (policy)
            {
                case ResolutionPolicy.LowestAvailable:
                    chosen = available.OrderBy(m => (long)m.Width * m.Height).FirstOrDefault();
                    break;
                case ResolutionPolicy.HighestAvailable:
                    chosen = available.FirstOrDefault();
                    break;
                case ResolutionPolicy.Exact:
                    if (preferredWidth > 0 && preferredHeight > 0)
                    {
                        chosen = available.FirstOrDefault(m =>
                            m.Width == preferredWidth.Value && m.Height == preferredHeight.Value);
                    }
                    break;
            }

            if (chosen == null)
            {
                return new ResolutionPlan
                {
                    ShouldApply = false,
                    EffectivePolicy = policy,
                    Reason = "mode unavailable"
                };
            }

            if (chosen.Width == target.Width && chosen.Height == target.Height)
            {
                return new ResolutionPlan
                {
                    ShouldApply = false,
                    TargetWidth = chosen.Width,
                    TargetHeight = chosen.Height,
                    EffectivePolicy = policy,
                    Reason = "already at target"
                };
            }

            return new ResolutionPlan
            {
                ShouldApply = true,
                TargetWidth = chosen.Width,
                TargetHeight = chosen.Height,
                EffectivePolicy = policy,
                Reason = chosen.Label
            };
        }

        public bool TryApply(DisplayInfo target, int width, int height, out string error)
        {
            error = null;
            if (target == null || string.IsNullOrWhiteSpace(target.GdiDeviceName) || width <= 0 || height <= 0)
            {
                error = "Invalid resolution target.";
                return false;
            }

            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(target.GdiDeviceName, EnumCurrentSettings, ref mode))
            {
                error = "EnumDisplaySettings failed for current mode.";
                return false;
            }

            mode.dmPelsWidth = width;
            mode.dmPelsHeight = height;
            mode.dmFields |= DmPelsWidth | DmPelsHeight;
            if (mode.dmDisplayFrequency > 1)
            {
                mode.dmFields |= DmDisplayFrequency;
            }

            var test = ChangeDisplaySettingsEx(target.GdiDeviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
            if (test != 0)
            {
                error = "Resolution test failed (" + test + ").";
                return false;
            }

            var apply = ChangeDisplaySettingsEx(
                target.GdiDeviceName,
                ref mode,
                IntPtr.Zero,
                CdsUpdateregistry,
                IntPtr.Zero);
            if (apply != 0)
            {
                error = "Resolution apply failed (" + apply + ").";
                return false;
            }

            return true;
        }

        private static ResolutionPolicy ResolvePolicy(
            ResolutionPolicy globalPolicy,
            GameResolutionOverride gameOverride)
        {
            switch (gameOverride)
            {
                case GameResolutionOverride.Native:
                    return ResolutionPolicy.Native;
                case GameResolutionOverride.Exact:
                    return ResolutionPolicy.Exact;
                case GameResolutionOverride.LowestAvailable:
                    return ResolutionPolicy.LowestAvailable;
                case GameResolutionOverride.HighestAvailable:
                    return ResolutionPolicy.HighestAvailable;
                default:
                    return globalPolicy;
            }
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
            private const int CchDevicename = 32;
            private const int CchFormname = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDevicename)]
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
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormname)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
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
