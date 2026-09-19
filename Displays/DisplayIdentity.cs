using System;

namespace PlayniteDisplayManager.Displays
{
    public static class DisplayIdentity
    {
        public const string EdidPrefix = "edid:";
        public const string ConnectorPrefix = "connector:";

        public static string BuildStableId(
            bool edidIdsValid,
            ushort manufactureId,
            ushort productCodeId,
            uint? serial,
            string adapterPath,
            uint connectorInstance,
            string monitorDevicePath = null)
        {
            var adapter = string.IsNullOrWhiteSpace(adapterPath) ? "unknown-adapter" : adapterPath.Trim();
            var connectorKey = adapter + "|" + connectorInstance;

            if (edidIdsValid && (manufactureId != 0 || productCodeId != 0))
            {
                if (serial.HasValue && serial.Value != 0)
                {
                    return EdidPrefix + manufactureId.ToString("X4") + "-" + productCodeId.ToString("X4") + "-" +
                           serial.Value.ToString("X8");
                }

                // Identical panels often share manufacture/product with serial 0 — disambiguate by connector.
                return EdidPrefix + manufactureId.ToString("X4") + "-" + productCodeId.ToString("X4") +
                       "-NOSERIAL|" + connectorKey;
            }

            return ConnectorPrefix + connectorKey;
        }

        /// <summary>
        /// Append a stable PnP-path suffix when two targets still collide after the primary id rules.
        /// </summary>
        public static string Disambiguate(string id, string monitorDevicePath)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(monitorDevicePath))
            {
                return id;
            }

            var hash = monitorDevicePath.Trim().GetHashCode().ToString("X8");
            if (id.IndexOf("#" + hash, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return id;
            }

            return id + "#" + hash;
        }

        public static string FormatManufacturerCode(ushort manufactureId)
        {
            // EDID manufacturer id is big-endian packed 5-bit letters.
            var swapped = (ushort)(((manufactureId & 0xff00) >> 8) | ((manufactureId & 0x00ff) << 8));
            var c1 = (char)('A' + ((swapped >> 10) & 0x1f) - 1);
            var c2 = (char)('A' + ((swapped >> 5) & 0x1f) - 1);
            var c3 = (char)('A' + (swapped & 0x1f) - 1);
            if (c1 < 'A' || c1 > 'Z' || c2 < 'A' || c2 > 'Z' || c3 < 'A' || c3 > 'Z')
            {
                return manufactureId.ToString("X4");
            }

            return new string(new[] { c1, c2, c3 });
        }

        public static bool IsEdidIdentity(string id)
        {
            return !string.IsNullOrWhiteSpace(id) &&
                   id.StartsWith(EdidPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
