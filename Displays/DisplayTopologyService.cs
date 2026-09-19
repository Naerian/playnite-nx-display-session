using System;
using System.Collections.Generic;
using System.Diagnostics;
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

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.WriteLine(ex);
                return false;
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
