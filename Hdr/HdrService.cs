using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using static PlayniteDisplayManager.Displays.DisplayNative;

namespace PlayniteDisplayManager.Hdr
{
    public enum GlobalHdrPolicy
    {
        /// <summary>Option 1 — do not manage HDR globally.</summary>
        DoNotManage = 0,

        /// <summary>Option 2 — HDR on for any game; write off on exit.</summary>
        OnForAllGames = 1,

        /// <summary>Option 3 — HDR only when game metadata matches (step 7).</summary>
        OnWhenMetadataIndicates = 2
    }

    [DataContract]
    public sealed class HdrWriteTarget
    {
        [DataMember(Name = "adapterLow")]
        public uint AdapterIdLow { get; set; }

        [DataMember(Name = "adapterHigh")]
        public int AdapterIdHigh { get; set; }

        [DataMember(Name = "targetId")]
        public uint TargetId { get; set; }

        /// <summary>Value to WRITE on restore — never derived from a trusted readback under ACM.</summary>
        [DataMember(Name = "enable")]
        public bool Enable { get; set; }

        [DataMember(Name = "displayId")]
        public string DisplayId { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }
    }

    public sealed class HdrTargetInfo
    {
        public string DisplayId { get; set; }
        public string Name { get; set; }
        public uint AdapterIdLow { get; set; }
        public int AdapterIdHigh { get; set; }
        public uint TargetId { get; set; }
        public bool AdvancedColorSupported { get; set; }
        /// <summary>Untrusted under ACM — for diagnostics only.</summary>
        public bool AdvancedColorEnabledUntrusted { get; set; }
        public bool WideColorEnforcedUntrusted { get; set; }
        public bool IsPrimary { get; set; }
    }

    /// <summary>
    /// HDR ownership via CCD write APIs. Restore always WRITES the desired state;
    /// GET_ADVANCED_COLOR_INFO is treated as untrusted under Automatic Color Management.
    /// </summary>
    public sealed class HdrService
    {
        public IReadOnlyList<HdrTargetInfo> ProbeActiveTargets(IEnumerable<Displays.DisplayInfo> displays)
        {
            var results = new List<HdrTargetInfo>();
            var list = (displays ?? Enumerable.Empty<Displays.DisplayInfo>())
                .Where(d => d != null && d.IsConnected)
                .ToList();

            foreach (var display in list)
            {
                var info = TryGetAdvancedColorInfo(display.AdapterIdLow, display.AdapterIdHigh, display.TargetId);
                if (info == null)
                {
                    continue;
                }

                results.Add(new HdrTargetInfo
                {
                    DisplayId = display.Id,
                    Name = display.EffectiveName,
                    AdapterIdLow = display.AdapterIdLow,
                    AdapterIdHigh = display.AdapterIdHigh,
                    TargetId = display.TargetId,
                    AdvancedColorSupported = info.Value.AdvancedColorSupported,
                    AdvancedColorEnabledUntrusted = info.Value.AdvancedColorEnabled,
                    WideColorEnforcedUntrusted = info.Value.WideColorEnforced,
                    IsPrimary = display.IsPrimary
                });
            }

            return results;
        }

        public bool TryWriteAdvancedColor(uint adapterLow, int adapterHigh, uint targetId, bool enable, out string error)
        {
            error = null;
            var set = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                    size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE)),
                    adapterId = new LUID { LowPart = adapterLow, HighPart = adapterHigh },
                    id = targetId
                },
                enableAdvancedColor = enable ? 1u : 0u
            };

            var result = DisplayConfigSetDeviceInfo(ref set);
            if (result == ERROR_SUCCESS)
            {
                return true;
            }

            // Newer Win11 path — best-effort fallback.
            var hdr = new DISPLAYCONFIG_SET_HDR_STATE
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE,
                    size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_HDR_STATE)),
                    adapterId = new LUID { LowPart = adapterLow, HighPart = adapterHigh },
                    id = targetId
                },
                enableHdr = enable ? 1u : 0u
            };
            var hdrResult = DisplayConfigSetDeviceInfo(ref hdr);
            if (hdrResult == ERROR_SUCCESS)
            {
                return true;
            }

            error = "DisplayConfigSetDeviceInfo failed (advancedColor=" + result + ", hdrState=" + hdrResult + ").";
            Debug.WriteLine("Display Manager HDR: " + error);
            return false;
        }

        public bool TryWriteAdvancedColor(HdrWriteTarget target, out string error)
        {
            if (target == null)
            {
                error = "HDR write target is null.";
                return false;
            }

            return TryWriteAdvancedColor(
                target.AdapterIdLow,
                target.AdapterIdHigh,
                target.TargetId,
                target.Enable,
                out error);
        }

        public int ApplyHdrWrites(IEnumerable<HdrWriteTarget> writes, out string error)
        {
            error = null;
            var count = 0;
            var errors = new List<string>();
            foreach (var write in writes ?? Enumerable.Empty<HdrWriteTarget>())
            {
                if (write == null)
                {
                    continue;
                }

                if (TryWriteAdvancedColor(write, out var writeError))
                {
                    count++;
                }
                else if (!string.IsNullOrWhiteSpace(writeError))
                {
                    errors.Add(writeError);
                }
            }

            if (count == 0 && errors.Count > 0)
            {
                error = string.Join(" ", errors);
            }

            return count;
        }

        /// <summary>
        /// Build restore writes that force HDR off on capable targets (policy 2 exit / lease).
        /// Does not trust current GET state.
        /// </summary>
        public List<HdrWriteTarget> BuildForceOffWrites(IEnumerable<Displays.DisplayInfo> displays)
        {
            var writes = new List<HdrWriteTarget>();
            foreach (var probe in ProbeActiveTargets(displays))
            {
                if (!probe.AdvancedColorSupported)
                {
                    continue;
                }

                writes.Add(new HdrWriteTarget
                {
                    AdapterIdLow = probe.AdapterIdLow,
                    AdapterIdHigh = probe.AdapterIdHigh,
                    TargetId = probe.TargetId,
                    Enable = false,
                    DisplayId = probe.DisplayId,
                    Name = probe.Name
                });
            }

            return writes;
        }

        public List<HdrWriteTarget> BuildEnableWritesForPrimary(IEnumerable<Displays.DisplayInfo> displays)
        {
            var writes = new List<HdrWriteTarget>();
            foreach (var probe in ProbeActiveTargets(displays).Where(p => p.IsPrimary && p.AdvancedColorSupported))
            {
                writes.Add(new HdrWriteTarget
                {
                    AdapterIdLow = probe.AdapterIdLow,
                    AdapterIdHigh = probe.AdapterIdHigh,
                    TargetId = probe.TargetId,
                    Enable = true,
                    DisplayId = probe.DisplayId,
                    Name = probe.Name
                });
            }

            return writes;
        }

        private static DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO? TryGetAdvancedColorInfo(
            uint adapterLow, int adapterHigh, uint targetId)
        {
            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)),
                    adapterId = new LUID { LowPart = adapterLow, HighPart = adapterHigh },
                    id = targetId
                }
            };

            var result = DisplayConfigGetDeviceInfo(ref info);
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            return info;
        }
    }
}
