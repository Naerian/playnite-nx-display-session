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

        /// <summary>Lowest area mode the driver reports for the target (incl. custom).</summary>
        LowestAvailable = 2,

        /// <summary>Highest area mode the driver reports for the target (incl. custom).</summary>
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

        /// <summary>
        /// True when the mode is exposed by the driver but not advertised in the monitor EDID
        /// (scaled modes, NVIDIA/AMD/CRU custom timings, etc.).
        /// </summary>
        public bool IsCustom { get; set; }

        /// <summary>Localized group header for UI combos; set by the settings view.</summary>
        public string GroupName { get; set; }

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
        // Include driver-reported modes that are not in the monitor EDID
        // (NVIDIA/AMD/CRU custom resolutions). Vendor-agnostic Win32 flag.
        private const uint EdsRawMode = 0x00000002;
        private const int CdsEnableUnsafeModes = 0x00000100;
        private const uint DmPelsWidth = 0x00080000;
        private const uint DmPelsHeight = 0x00100000;
        private const uint DmDisplayFrequency = 0x00400000;

        private readonly object cacheLock = new object();
        private readonly Dictionary<string, IReadOnlyList<ResolutionMode>> modesCache =
            new Dictionary<string, IReadOnlyList<ResolutionMode>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Drop cached EnumDisplaySettings results (call when topology changes).</summary>
        public void ClearCache()
        {
            lock (cacheLock)
            {
                modesCache.Clear();
            }
        }

        public IReadOnlyList<ResolutionMode> GetAvailableModes(DisplayInfo display)
        {
            if (display == null || string.IsNullOrWhiteSpace(display.GdiDeviceName))
            {
                return new List<ResolutionMode>();
            }

            // CRT/CRU setups can expose thousands of RAWMODE entries; enumerating is expensive.
            var cacheKey = display.GdiDeviceName + "\n" + (display.MonitorDevicePath ?? string.Empty);
            lock (cacheLock)
            {
                if (modesCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }
            }

            var modes = new SortedDictionary<long, ResolutionMode>();
            var device = display.GdiDeviceName;
            var edidKeys = DisplayEdidReader.TryReadResolutionKeys(display.MonitorDevicePath);
            var plainKeys = CollectModeKeys(device, raw: false);

            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));

            // EDS_RAWMODE: all modes the adapter driver reports, including custom
            // timings registered via NVIDIA CP / AMD / CRU that EnumDisplaySettings
            // would otherwise filter as "incompatible with the monitor".
            for (var i = 0; EnumDisplaySettingsEx(device, i, ref mode, EdsRawMode); i++)
            {
                mode.dmDriverExtra = 0;
                if (mode.dmPelsWidth < 640 || mode.dmPelsHeight < 480)
                {
                    continue;
                }

                var key = DisplayEdidReader.ToKey(mode.dmPelsWidth, mode.dmPelsHeight);
                if (modes.ContainsKey(key))
                {
                    continue;
                }

                var onlyInRaw = plainKeys != null && !plainKeys.Contains(key);
                var missingFromEdid = edidKeys != null && !edidKeys.Contains(key);
                modes[key] = new ResolutionMode
                {
                    Width = mode.dmPelsWidth,
                    Height = mode.dmPelsHeight,
                    IsCustom = onlyInRaw || missingFromEdid
                };
            }

            // Monitor (EDID) first, then custom/driver extras; largest area within each group.
            IReadOnlyList<ResolutionMode> result = modes.Values
                .OrderBy(m => m.IsCustom)
                .ThenByDescending(m => (long)m.Width * m.Height)
                .ToList();

            lock (cacheLock)
            {
                modesCache[cacheKey] = result;
            }

            return result;
        }

        private static HashSet<long> CollectModeKeys(string device, bool raw)
        {
            var keys = new HashSet<long>();
            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            for (var i = 0; ; i++)
            {
                mode.dmDriverExtra = 0;
                var ok = raw
                    ? EnumDisplaySettingsEx(device, i, ref mode, EdsRawMode)
                    : EnumDisplaySettings(device, i, ref mode);
                if (!ok)
                {
                    break;
                }

                if (mode.dmPelsWidth < 640 || mode.dmPelsHeight < 480)
                {
                    continue;
                }

                keys.Add(DisplayEdidReader.ToKey(mode.dmPelsWidth, mode.dmPelsHeight));
            }

            return keys;
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

            var applyFlags = CdsUpdateregistry;
            var test = ChangeDisplaySettingsEx(target.GdiDeviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
            if (test != 0)
            {
                // Custom timings often fail the safe CDS_TEST; retry as unsafe mode
                // (same path Windows uses after NVIDIA/AMD custom resolutions are enabled).
                test = ChangeDisplaySettingsEx(
                    target.GdiDeviceName,
                    ref mode,
                    IntPtr.Zero,
                    CdsTest | CdsEnableUnsafeModes,
                    IntPtr.Zero);
                if (test != 0)
                {
                    error = "Resolution test failed (" + test + ").";
                    return false;
                }

                applyFlags |= CdsEnableUnsafeModes;
            }

            var apply = ChangeDisplaySettingsEx(
                target.GdiDeviceName,
                ref mode,
                IntPtr.Zero,
                applyFlags,
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
        private static extern bool EnumDisplaySettingsEx(
            string lpszDeviceName,
            int iModeNum,
            ref DEVMODE lpDevMode,
            uint dwFlags);

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
