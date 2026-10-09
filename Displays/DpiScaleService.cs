using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using static PlayniteDisplayManager.Displays.DisplayNative;

namespace PlayniteDisplayManager.Displays
{
    /// <summary>
    /// Absolute Windows UI scale (%) to write on restore for a display source.
    /// </summary>
    [DataContract]
    public sealed class DpiWriteTarget
    {
        [DataMember(Name = "adapterLow")]
        public uint AdapterIdLow { get; set; }

        [DataMember(Name = "adapterHigh")]
        public int AdapterIdHigh { get; set; }

        [DataMember(Name = "sourceId")]
        public uint SourceId { get; set; }

        [DataMember(Name = "scalePercent")]
        public uint ScalePercent { get; set; }

        [DataMember(Name = "displayId")]
        public string DisplayId { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }
    }

    public sealed class DpiScaleInfo
    {
        public uint Current { get; set; }
        public uint Recommended { get; set; }
        public uint Minimum { get; set; }
        public uint Maximum { get; set; }
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// Read/write per-monitor Windows display scaling via undocumented CCD DPI packets
    /// (DisplayConfigGet/SetDeviceInfo types -3 / -4). Same approach as SetDPI / community samples.
    /// Not a public Microsoft API — treat as best-effort and always pair with restore writes.
    /// </summary>
    public sealed class DpiScaleService
    {
        /// <summary>Known Windows UI scale steps (percent). Must stay contiguous for relative math.</summary>
        private static readonly uint[] DpiPercents =
        {
            100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500
        };

        public DpiScaleInfo TryGet(DisplayInfo display)
        {
            if (display == null)
            {
                return new DpiScaleInfo();
            }

            return TryGet(display.AdapterIdLow, display.AdapterIdHigh, display.SourceId);
        }

        public DpiScaleInfo TryGet(uint adapterLow, int adapterHigh, uint sourceId)
        {
            var info = new DpiScaleInfo();
            if (sourceId == 0 && adapterLow == 0 && adapterHigh == 0)
            {
                return info;
            }

            if (Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_GET)) != DISPLAYCONFIG_SOURCE_DPI_SCALE_GET_SIZE)
            {
                Debug.WriteLine("Display Manager: DPI GET struct size mismatch — OS layout may have changed.");
                return info;
            }

            var request = new DISPLAYCONFIG_SOURCE_DPI_SCALE_GET
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE,
                    size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_GET)),
                    adapterId = new LUID { LowPart = adapterLow, HighPart = adapterHigh },
                    id = sourceId
                }
            };

            var result = DisplayConfigGetDeviceInfo(ref request);
            if (result != ERROR_SUCCESS)
            {
                return info;
            }

            var curRel = request.curScaleRel;
            if (curRel < request.minScaleRel)
            {
                curRel = request.minScaleRel;
            }
            else if (curRel > request.maxScaleRel)
            {
                curRel = request.maxScaleRel;
            }

            var minAbs = Math.Abs(request.minScaleRel);
            if (DpiPercents.Length < minAbs + request.maxScaleRel + 1)
            {
                Debug.WriteLine("Display Manager: DPI percent table outdated for this source.");
                return info;
            }

            info.Minimum = DpiPercents[minAbs + request.minScaleRel];
            info.Recommended = DpiPercents[minAbs];
            info.Current = DpiPercents[minAbs + curRel];
            info.Maximum = DpiPercents[minAbs + request.maxScaleRel];
            info.IsValid = true;
            return info;
        }

        public bool TrySet(DisplayInfo display, uint scalePercent, out string error)
        {
            error = null;
            if (display == null)
            {
                error = "No display specified for DPI scale.";
                return false;
            }

            return TrySet(display.AdapterIdLow, display.AdapterIdHigh, display.SourceId, scalePercent, out error);
        }

        public bool TrySet(uint adapterLow, int adapterHigh, uint sourceId, uint scalePercent, out string error)
        {
            error = null;
            var current = TryGet(adapterLow, adapterHigh, sourceId);
            if (!current.IsValid)
            {
                error = "Could not read current DPI scale for the display source.";
                return false;
            }

            if (scalePercent == current.Current)
            {
                return true;
            }

            if (scalePercent < current.Minimum)
            {
                scalePercent = current.Minimum;
            }
            else if (scalePercent > current.Maximum)
            {
                scalePercent = current.Maximum;
            }

            var targetIndex = IndexOfPercent(scalePercent);
            var recommendedIndex = IndexOfPercent(current.Recommended);
            if (targetIndex < 0 || recommendedIndex < 0)
            {
                error = "DPI scale percent is not in the known Windows scale table.";
                return false;
            }

            if (Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_SET)) != DISPLAYCONFIG_SOURCE_DPI_SCALE_SET_SIZE)
            {
                error = "DPI SET struct size mismatch — OS layout may have changed.";
                return false;
            }

            var setPacket = new DISPLAYCONFIG_SOURCE_DPI_SCALE_SET
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE,
                    size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_SET)),
                    adapterId = new LUID { LowPart = adapterLow, HighPart = adapterHigh },
                    id = sourceId
                },
                scaleRel = targetIndex - recommendedIndex
            };

            var result = DisplayConfigSetDeviceInfo(ref setPacket);
            if (result != ERROR_SUCCESS)
            {
                error = "DisplayConfigSetDeviceInfo(DPI) failed with code " + result + ".";
                return false;
            }

            return true;
        }

        public DpiWriteTarget BuildRestoreWrite(DisplayInfo display, uint scalePercent)
        {
            if (display == null)
            {
                return null;
            }

            return new DpiWriteTarget
            {
                AdapterIdLow = display.AdapterIdLow,
                AdapterIdHigh = display.AdapterIdHigh,
                SourceId = display.SourceId,
                ScalePercent = scalePercent,
                DisplayId = display.Id,
                Name = display.EffectiveName
            };
        }

        public int ApplyDpiWrites(IEnumerable<DpiWriteTarget> writes, IEnumerable<DisplayInfo> liveDisplays, out string error)
        {
            error = null;
            var applied = 0;
            var errors = new List<string>();
            var live = (liveDisplays ?? Enumerable.Empty<DisplayInfo>()).Where(d => d != null && d.IsConnected).ToList();

            foreach (var write in writes ?? Enumerable.Empty<DpiWriteTarget>())
            {
                if (write == null || write.ScalePercent == 0)
                {
                    continue;
                }

                uint adapterLow = write.AdapterIdLow;
                int adapterHigh = write.AdapterIdHigh;
                uint sourceId = write.SourceId;

                // Prefer live source ids when the display is still present (topology restore may reshuffle).
                if (!string.IsNullOrWhiteSpace(write.DisplayId))
                {
                    var match = live.FirstOrDefault(d =>
                        string.Equals(d.Id, write.DisplayId, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        adapterLow = match.AdapterIdLow;
                        adapterHigh = match.AdapterIdHigh;
                        sourceId = match.SourceId;
                    }
                }

                if (!TrySet(adapterLow, adapterHigh, sourceId, write.ScalePercent, out var writeError))
                {
                    errors.Add((write.Name ?? write.DisplayId ?? "display") + ": " + writeError);
                    continue;
                }

                applied++;
            }

            if (errors.Count > 0)
            {
                error = string.Join(" | ", errors);
            }

            return applied;
        }

        private static int IndexOfPercent(uint percent)
        {
            for (var i = 0; i < DpiPercents.Length; i++)
            {
                if (DpiPercents[i] == percent)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
