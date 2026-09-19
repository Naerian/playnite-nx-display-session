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
            uint connectorInstance)
        {
            if (edidIdsValid && (manufactureId != 0 || productCodeId != 0))
            {
                var serialPart = serial.HasValue && serial.Value != 0
                    ? serial.Value.ToString("X8")
                    : "NOSERIAL";
                return $"{EdidPrefix}{manufactureId:X4}-{productCodeId:X4}-{serialPart}";
            }

            var adapter = string.IsNullOrWhiteSpace(adapterPath) ? "unknown-adapter" : adapterPath.Trim();
            return $"{ConnectorPrefix}{adapter}|{connectorInstance}";
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
