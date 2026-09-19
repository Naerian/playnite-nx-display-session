using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using static PlayniteDisplayManager.Displays.DisplayNative;

namespace PlayniteDisplayManager.Displays
{
    /// <summary>
    /// Capture / restore Windows display topology via CCD. Shared by the plugin and RestoreHost.
    /// Does not reference Playnite SDK so the external host stays lean.
    /// </summary>
    public sealed class DisplayTopologyService
    {
        private readonly DisplayEnumerator enumerator;

        public DisplayTopologyService()
        {
            enumerator = new DisplayEnumerator();
        }

        public DisplaySnapshot CaptureSnapshot()
        {
            var flags = QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE;
            if (!TryQuery(flags, out var paths, out var modes))
            {
                flags = QDC_ONLY_ACTIVE_PATHS;
                if (!TryQuery(flags, out paths, out modes))
                {
                    throw new InvalidOperationException("QueryDisplayConfig failed while capturing a display snapshot.");
                }
            }

            var snapshot = new DisplaySnapshot
            {
                CapturedUtc = DateTime.UtcNow.ToString("o"),
                QueryFlags = flags,
                PathCount = paths.Length,
                ModeCount = modes.Length,
                PathsBase64 = Convert.ToBase64String(StructArrayToBytes(paths)),
                ModesBase64 = Convert.ToBase64String(StructArrayToBytes(modes)),
                Displays = new List<DisplaySnapshotEntry>()
            };

            foreach (var display in enumerator.GetDisplays())
            {
                snapshot.Displays.Add(new DisplaySnapshotEntry
                {
                    Id = display.Id,
                    Name = display.Name,
                    IsPrimary = display.IsPrimary,
                    GdiDeviceName = display.GdiDeviceName,
                    Width = display.Width,
                    Height = display.Height
                });
            }

            return snapshot;
        }

        public DisplayTopologyApplyResult TryApplyRequest(DisplayTopologyRequest request)
        {
            var result = new DisplayTopologyApplyResult();
            if (request == null || string.IsNullOrWhiteSpace(request.TargetDisplayId))
            {
                result.Error = "No target display was specified.";
                return result;
            }

            if (!request.MakePrimary && !request.TurnOffOtherDisplays)
            {
                result.Error = "Nothing to apply (make primary and turn off others are both off).";
                return result;
            }

            DisplaySnapshot before;
            try
            {
                before = CaptureSnapshot();
            }
            catch (Exception ex)
            {
                result.Error = "Could not capture snapshot before apply: " + ex.Message;
                return result;
            }

            result.BeforeSnapshot = before;

            var queryFlags = before.QueryFlags != 0
                ? before.QueryFlags
                : (QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE);
            if (!TryQuery(queryFlags, out var paths, out var modes) &&
                !TryQuery(QDC_ONLY_ACTIVE_PATHS, out paths, out modes))
            {
                result.Error = "QueryDisplayConfig failed before apply.";
                return result;
            }

            var displays = enumerator.GetDisplays();
            var target = displays.FirstOrDefault(d =>
                d.IsConnected &&
                string.Equals(d.Id, request.TargetDisplayId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                result.Error = "Target display is not connected: " + request.TargetDisplayId;
                return result;
            }

            var targetPathIndex = -1;
            for (var i = 0; i < paths.Length; i++)
            {
                if ((paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) == 0)
                {
                    continue;
                }

                if (paths[i].targetInfo.adapterId.LowPart == target.AdapterIdLow &&
                    paths[i].targetInfo.adapterId.HighPart == target.AdapterIdHigh &&
                    paths[i].targetInfo.id == target.TargetId)
                {
                    targetPathIndex = i;
                    break;
                }
            }

            if (targetPathIndex < 0)
            {
                result.Error = "Could not match the target display to an active CCD path.";
                return result;
            }

            var activeCount = paths.Count(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0);
            if (request.TurnOffOtherDisplays && activeCount <= 1)
            {
                result.Error = "Turn off other displays was skipped: only one active display is present.";
                // Still allow make-primary alone.
                if (!request.MakePrimary)
                {
                    return result;
                }

                request = new DisplayTopologyRequest
                {
                    TargetDisplayId = request.TargetDisplayId,
                    MakePrimary = true,
                    TurnOffOtherDisplays = false
                };
            }

            if (request.TurnOffOtherDisplays)
            {
                for (var i = 0; i < paths.Length; i++)
                {
                    if (i == targetPathIndex)
                    {
                        continue;
                    }

                    if ((paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) != 0)
                    {
                        paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
                    }
                }

                // Sacred rule: never leave zero active paths.
                if ((paths[targetPathIndex].flags & DISPLAYCONFIG_PATH_ACTIVE) == 0)
                {
                    paths[targetPathIndex].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                }

                var remaining = paths.Count(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0);
                if (remaining == 0)
                {
                    result.Error = "Refusing to apply a topology with zero active displays.";
                    return result;
                }
            }

            if (request.MakePrimary)
            {
                if (!TryMakePathPrimary(paths, modes, targetPathIndex, out var primaryError))
                {
                    result.Error = primaryError;
                    return result;
                }
            }

            if (!TrySetDisplayConfig(paths, modes, queryFlags, out var applyError))
            {
                result.Error = applyError;
                TryRestoreSnapshot(before, out _);
                return result;
            }

            result.Success = true;
            result.Message = BuildApplyMessage(request, target);
            return result;
        }

        private static string BuildApplyMessage(DisplayTopologyRequest request, DisplayInfo target)
        {
            var parts = new List<string>();
            if (request.MakePrimary)
            {
                parts.Add("primary → " + target.EffectiveName);
            }

            if (request.TurnOffOtherDisplays)
            {
                parts.Add("other displays off");
            }

            return string.Join("; ", parts);
        }

        private static bool TryMakePathPrimary(
            DISPLAYCONFIG_PATH_INFO[] paths,
            DISPLAYCONFIG_MODE_INFO[] modes,
            int targetPathIndex,
            out string error)
        {
            error = null;
            if (!TryGetSourceModeIndex(paths[targetPathIndex], modes, out var targetModeIndex))
            {
                error = "Target path has no source mode.";
                return false;
            }

            var originX = modes[targetModeIndex].sourceMode.position.x;
            var originY = modes[targetModeIndex].sourceMode.position.y;
            if (originX == 0 && originY == 0)
            {
                return true; // already primary
            }

            // Translate every source mode used by an active path so the target lands at (0,0).
            var touched = new HashSet<int>();
            for (var i = 0; i < paths.Length; i++)
            {
                if ((paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) == 0)
                {
                    continue;
                }

                if (!TryGetSourceModeIndex(paths[i], modes, out var modeIndex) || !touched.Add(modeIndex))
                {
                    continue;
                }

                var mode = modes[modeIndex];
                mode.sourceMode.position.x -= originX;
                mode.sourceMode.position.y -= originY;
                modes[modeIndex] = mode;
            }

            return true;
        }

        private static bool TryGetSourceModeIndex(
            DISPLAYCONFIG_PATH_INFO path,
            DISPLAYCONFIG_MODE_INFO[] modes,
            out int modeIndex)
        {
            modeIndex = -1;
            if (modes == null || modes.Length == 0)
            {
                return false;
            }

            var packed = (int)(path.sourceInfo.modeInfoIdx & 0xFFFF);
            if (packed >= 0 && packed < modes.Length &&
                modes[packed].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                modeIndex = packed;
                return true;
            }

            for (var i = 0; i < modes.Length; i++)
            {
                if (modes[i].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                {
                    continue;
                }

                if (modes[i].id == path.sourceInfo.id &&
                    modes[i].adapterId.LowPart == path.sourceInfo.adapterId.LowPart &&
                    modes[i].adapterId.HighPart == path.sourceInfo.adapterId.HighPart)
                {
                    modeIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static bool TrySetDisplayConfig(
            DISPLAYCONFIG_PATH_INFO[] paths,
            DISPLAYCONFIG_MODE_INFO[] modes,
            uint queryFlags,
            out string error)
        {
            error = null;
            var flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;
            if ((queryFlags & QDC_VIRTUAL_MODE_AWARE) != 0)
            {
                flags |= SDC_VIRTUAL_MODE_AWARE;
            }

            var result = SetDisplayConfig(
                (uint)paths.Length, paths, (uint)modes.Length, modes, flags);
            if (result != ERROR_SUCCESS)
            {
                var retry = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG;
                if ((queryFlags & QDC_VIRTUAL_MODE_AWARE) != 0)
                {
                    retry |= SDC_VIRTUAL_MODE_AWARE;
                }

                result = SetDisplayConfig(
                    (uint)paths.Length, paths, (uint)modes.Length, modes, retry);
            }

            if (result != ERROR_SUCCESS)
            {
                error = "SetDisplayConfig failed with code " + result + ".";
                return false;
            }

            return true;
        }

        public bool TryRestoreSnapshot(DisplaySnapshot snapshot, out string error)
        {
            error = null;
            if (snapshot == null ||
                string.IsNullOrWhiteSpace(snapshot.PathsBase64) ||
                string.IsNullOrWhiteSpace(snapshot.ModesBase64) ||
                snapshot.PathCount <= 0)
            {
                error = "Snapshot is empty or invalid.";
                return false;
            }

            try
            {
                var paths = BytesToStructArray<DISPLAYCONFIG_PATH_INFO>(
                    Convert.FromBase64String(snapshot.PathsBase64), snapshot.PathCount);
                var modes = BytesToStructArray<DISPLAYCONFIG_MODE_INFO>(
                    Convert.FromBase64String(snapshot.ModesBase64), snapshot.ModeCount);

                var flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;
                if ((snapshot.QueryFlags & QDC_VIRTUAL_MODE_AWARE) != 0)
                {
                    flags |= SDC_VIRTUAL_MODE_AWARE;
                }

                var result = SetDisplayConfig(
                    (uint)paths.Length,
                    paths,
                    (uint)modes.Length,
                    modes,
                    flags);

                if (result != ERROR_SUCCESS)
                {
                    var retry = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG;
                    if ((snapshot.QueryFlags & QDC_VIRTUAL_MODE_AWARE) != 0)
                    {
                        retry |= SDC_VIRTUAL_MODE_AWARE;
                    }

                    result = SetDisplayConfig(
                        (uint)paths.Length,
                        paths,
                        (uint)modes.Length,
                        modes,
                        retry);
                }

                if (result != ERROR_SUCCESS)
                {
                    error = "SetDisplayConfig failed with code " + result + ".";
                    Debug.WriteLine("Display Manager: " + error);
                    return false;
                }

                ApplyHdrRestoreWrites(snapshot);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.WriteLine(ex);
                return false;
            }
        }

        private static void ApplyHdrRestoreWrites(DisplaySnapshot snapshot)
        {
            if (snapshot?.HdrRestoreWrites == null || snapshot.HdrRestoreWrites.Count == 0)
            {
                return;
            }

            var hdr = new Hdr.HdrService();
            hdr.ApplyHdrWrites(snapshot.HdrRestoreWrites, out var hdrError);
            if (!string.IsNullOrWhiteSpace(hdrError))
            {
                Debug.WriteLine("Display Manager HDR restore writes: " + hdrError);
            }
        }

        private static bool TryQuery(uint flags, out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
        {
            paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
            modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var sizeResult = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
                if (sizeResult != ERROR_SUCCESS)
                {
                    return false;
                }

                paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                var queryPathCount = pathCount;
                var queryModeCount = modeCount;
                var queryResult = QueryDisplayConfig(
                    flags,
                    ref queryPathCount,
                    paths,
                    ref queryModeCount,
                    modes,
                    IntPtr.Zero);

                if (queryResult == ERROR_SUCCESS)
                {
                    if (queryPathCount != paths.Length)
                    {
                        Array.Resize(ref paths, (int)queryPathCount);
                    }

                    if (queryModeCount != modes.Length)
                    {
                        Array.Resize(ref modes, (int)queryModeCount);
                    }

                    return true;
                }

                if (queryResult != ERROR_INSUFFICIENT_BUFFER)
                {
                    return false;
                }
            }

            return false;
        }

        private static byte[] StructArrayToBytes<T>(T[] array) where T : struct
        {
            if (array == null || array.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var stride = Marshal.SizeOf(typeof(T));
            var bytes = new byte[stride * array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                var ptr = Marshal.AllocHGlobal(stride);
                try
                {
                    Marshal.StructureToPtr(array[i], ptr, false);
                    Marshal.Copy(ptr, bytes, i * stride, stride);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            return bytes;
        }

        private static T[] BytesToStructArray<T>(byte[] bytes, int count) where T : struct
        {
            var stride = Marshal.SizeOf(typeof(T));
            if (bytes == null || count <= 0 || bytes.Length < stride * count)
            {
                throw new InvalidOperationException("Snapshot buffer size does not match the declared element count.");
            }

            var array = new T[count];
            for (var i = 0; i < count; i++)
            {
                var ptr = Marshal.AllocHGlobal(stride);
                try
                {
                    Marshal.Copy(bytes, i * stride, ptr, stride);
                    array[i] = (T)Marshal.PtrToStructure(ptr, typeof(T));
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            return array;
        }
    }
}
