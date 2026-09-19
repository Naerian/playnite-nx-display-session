using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using static PlayniteDisplayManager.Displays.DisplayNative;

namespace PlayniteDisplayManager.Displays
{
    public sealed class DisplayEnumerator
    {
        public DisplayEnumerator()
        {
        }

        /// <summary>
        /// Raw CCD snapshot of currently known targets (active paths preferred).
        /// </summary>
        public IReadOnlyList<DisplayInfo> GetDisplays()
        {
            try
            {
                var flags = QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE;
                if (!TryQuery(flags, out var paths, out var modes))
                {
                    flags = QDC_ONLY_ACTIVE_PATHS;
                    if (!TryQuery(flags, out paths, out modes))
                    {
                        Debug.WriteLine("Display Manager: QueryDisplayConfig failed; no displays enumerated.");
                        return Array.Empty<DisplayInfo>();
                    }
                }

                var primaryGdi = Screen.PrimaryScreen?.DeviceName;
                var results = new List<DisplayInfo>();
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < paths.Length; i++)
                {
                    var path = paths[i];
                    var isActive = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
                    if (!isActive && path.targetInfo.targetAvailable == 0)
                    {
                        continue;
                    }

                    var targetName = QueryTargetName(path.targetInfo.adapterId, path.targetInfo.id);
                    var sourceName = QuerySourceName(path.sourceInfo.adapterId, path.sourceInfo.id);
                    var adapterName = QueryAdapterName(path.targetInfo.adapterId);

                    var edidValid = (targetName.flags & DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS.EdidIdsValid) != 0;
                    ushort? mfg = edidValid ? targetName.edidManufactureId : (ushort?)null;
                    ushort? product = edidValid ? targetName.edidProductCodeId : (ushort?)null;
                    var serial = DisplayEdidReader.TryReadSerial(targetName.monitorDevicePath);

                    var id = DisplayIdentity.BuildStableId(
                        edidValid,
                        targetName.edidManufactureId,
                        targetName.edidProductCodeId,
                        serial,
                        adapterName.adapterDevicePath,
                        targetName.connectorInstance,
                        targetName.monitorDevicePath);

                    if (!seenIds.Add(id))
                    {
                        // Same EDID serial on two panels: harden with monitor PnP path.
                        id = DisplayIdentity.Disambiguate(id, targetName.monitorDevicePath);
                        if (!seenIds.Add(id))
                        {
                            continue;
                        }
                    }

                    GetSourceSize(path, modes, out var width, out var height);
                    if (width == 0 || height == 0)
                    {
                        TryFillSizeFromScreen(sourceName.viewGdiDeviceName, ref width, ref height);
                    }
                    var refresh = GetRefreshRateHz(path);

                    var friendly = targetName.monitorFriendlyDeviceName;
                    if (string.IsNullOrWhiteSpace(friendly))
                    {
                        friendly = !string.IsNullOrWhiteSpace(sourceName.viewGdiDeviceName)
                            ? sourceName.viewGdiDeviceName
                            : $"Display {results.Count + 1}";
                    }

                    var gdi = sourceName.viewGdiDeviceName ?? string.Empty;
                    var isPrimary = !string.IsNullOrWhiteSpace(primaryGdi) &&
                                    string.Equals(gdi, primaryGdi, StringComparison.OrdinalIgnoreCase);

                    results.Add(new DisplayInfo
                    {
                        Id = id,
                        Name = friendly.Trim(),
                        State = isActive ? DisplayConnectionState.Active : DisplayConnectionState.Inactive,
                        IsConnected = isActive || path.targetInfo.targetAvailable != 0,
                        IsPrimary = isPrimary,
                        IsVisible = true,
                        GdiDeviceName = gdi,
                        AdapterPath = adapterName.adapterDevicePath ?? string.Empty,
                        ConnectorInstance = targetName.connectorInstance,
                        MonitorDevicePath = targetName.monitorDevicePath ?? string.Empty,
                        IdentityKind = DisplayIdentity.IsEdidIdentity(id) ? "edid" : "connector",
                        EdidManufactureId = mfg,
                        EdidProductCodeId = product,
                        EdidSerial = serial,
                        Width = width,
                        Height = height,
                        RefreshRateHz = refresh,
                        AdapterIdLow = path.targetInfo.adapterId.LowPart,
                        AdapterIdHigh = path.targetInfo.adapterId.HighPart,
                        TargetId = path.targetInfo.id,
                        SourceId = path.sourceInfo.id
                    });
                }

                return results
                    .OrderByDescending(d => d.IsPrimary)
                    .ThenBy(d => d.EffectiveName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return Array.Empty<DisplayInfo>();
            }
        }

        /// <summary>
        /// Connected displays plus disconnected aliases the user customized.
        /// </summary>
        public IReadOnlyList<DisplayInfo> GetVisibleDisplays(IEnumerable<DisplayDeviceAlias> aliases)
        {
            var live = GetDisplays().Select(d => d.Clone()).ToList();
            var byId = live.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
            var aliasList = (aliases ?? Enumerable.Empty<DisplayDeviceAlias>()).ToList();

            foreach (var alias in aliasList)
            {
                if (string.IsNullOrWhiteSpace(alias?.DisplayId))
                {
                    continue;
                }

                if (byId.TryGetValue(alias.DisplayId, out var liveDisplay))
                {
                    ApplyAlias(liveDisplay, alias);
                    continue;
                }

                if (!HasMeaningfulAlias(alias))
                {
                    continue;
                }

                // Keep customized disconnected displays visible.
                var orphan = new DisplayInfo
                {
                    Id = alias.DisplayId,
                    Name = string.IsNullOrWhiteSpace(alias.LastKnownName) ? alias.DisplayId : alias.LastKnownName,
                    CustomName = SanitizeCustomName(alias.CustomName),
                    State = DisplayConnectionState.Inactive,
                    IsConnected = false,
                    IsPrimary = false,
                    IsVisible = alias.IsVisible != false,
                    IdentityKind = DisplayIdentity.IsEdidIdentity(alias.DisplayId) ? "edid" : "connector"
                };
                live.Add(orphan);
                byId[orphan.Id] = orphan;
            }

            return live
                .Where(d => d.IsConnected || HasMeaningfulAliasForDisplay(d, aliasList))
                .Where(d => d.IsVisible)
                .OrderByDescending(d => d.IsPrimary)
                .ThenByDescending(d => d.IsConnected)
                .ThenBy(d => d.EffectiveName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static void ApplyAlias(DisplayInfo display, DisplayDeviceAlias alias)
        {
            if (display == null || alias == null)
            {
                return;
            }

            display.CustomName = SanitizeCustomName(alias.CustomName);
            if (alias.IsVisible.HasValue)
            {
                display.IsVisible = alias.IsVisible.Value;
            }
        }

        public static bool HasMeaningfulAlias(DisplayDeviceAlias alias)
        {
            return alias != null &&
                   (!string.IsNullOrWhiteSpace(SanitizeCustomName(alias.CustomName)) ||
                    alias.IsVisible == false ||
                    !string.IsNullOrWhiteSpace(alias.Icon));
        }

        public static string SanitizeCustomName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static bool HasMeaningfulAliasForDisplay(DisplayInfo display, IEnumerable<DisplayDeviceAlias> aliases)
        {
            if (!string.IsNullOrWhiteSpace(SanitizeCustomName(display.CustomName)) || display.IsVisible == false)
            {
                return true;
            }

            return aliases.Any(a =>
                a != null &&
                string.Equals(a.DisplayId, display.Id, StringComparison.OrdinalIgnoreCase) &&
                HasMeaningfulAlias(a));
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

        private static DISPLAYCONFIG_TARGET_DEVICE_NAME QueryTargetName(LUID adapterId, uint targetId)
        {
            var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                    adapterId = adapterId,
                    id = targetId
                }
            };
            DisplayConfigGetDeviceInfo(ref deviceName);
            return deviceName;
        }

        private static DISPLAYCONFIG_SOURCE_DEVICE_NAME QuerySourceName(LUID adapterId, uint sourceId)
        {
            var deviceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)),
                    adapterId = adapterId,
                    id = sourceId
                }
            };
            DisplayConfigGetDeviceInfo(ref deviceName);
            return deviceName;
        }

        private static DISPLAYCONFIG_ADAPTER_NAME QueryAdapterName(LUID adapterId)
        {
            var adapterName = new DISPLAYCONFIG_ADAPTER_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAYCONFIG_ADAPTER_NAME)),
                    adapterId = adapterId,
                    id = 0
                }
            };
            DisplayConfigGetDeviceInfo(ref adapterName);
            return adapterName;
        }

        private static void TryFillSizeFromScreen(string gdiDeviceName, ref uint width, ref uint height)
        {
            if (string.IsNullOrWhiteSpace(gdiDeviceName))
            {
                return;
            }

            try
            {
                foreach (var screen in Screen.AllScreens)
                {
                    if (!string.Equals(screen.DeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    width = (uint)screen.Bounds.Width;
                    height = (uint)screen.Bounds.Height;
                    return;
                }
            }
            catch
            {
                // Screen bounds are informational only.
            }
        }

        private static void GetSourceSize(
            DISPLAYCONFIG_PATH_INFO path,
            DISPLAYCONFIG_MODE_INFO[] modes,
            out uint width,
            out uint height)
        {
            width = 0;
            height = 0;
            if (modes == null || modes.Length == 0)
            {
                return;
            }

            // Prefer match by adapter + source id; packed modeInfoIdx varies with VIRTUAL_MODE_AWARE.
            foreach (var mode in modes)
            {
                if (mode.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                {
                    continue;
                }

                if (mode.id == path.sourceInfo.id &&
                    mode.adapterId.LowPart == path.sourceInfo.adapterId.LowPart &&
                    mode.adapterId.HighPart == path.sourceInfo.adapterId.HighPart)
                {
                    width = mode.sourceMode.width;
                    height = mode.sourceMode.height;
                    return;
                }
            }

            var packedIndex = path.sourceInfo.modeInfoIdx & 0xFFFF;
            if (packedIndex < modes.Length &&
                modes[packedIndex].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                width = modes[packedIndex].sourceMode.width;
                height = modes[packedIndex].sourceMode.height;
            }
        }

        private static double GetRefreshRateHz(DISPLAYCONFIG_PATH_INFO path)
        {
            var rate = path.targetInfo.refreshRate;
            if (rate.Denominator == 0 || rate.Numerator == 0)
            {
                return 0;
            }

            return Math.Round((double)rate.Numerator / rate.Denominator, 2);
        }
    }
}
