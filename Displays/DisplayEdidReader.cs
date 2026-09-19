using System;
using Microsoft.Win32;

namespace PlayniteDisplayManager.Displays
{
    /// <summary>
    /// Reads the EDID serial from the monitor device registry when CCD does not expose it.
    /// </summary>
    public static class DisplayEdidReader
    {
        public static uint? TryReadSerial(string monitorDevicePath)
        {
            if (string.IsNullOrWhiteSpace(monitorDevicePath))
            {
                return null;
            }

            try
            {
                // Device path looks like:
                // \\?\DISPLAY#DELAxxx#5&...#{e6f07b5f-ee46-11d0-af4d-00a0c9062910}
                var normalized = monitorDevicePath.Trim();
                if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(4);
                }

                var hashIndex = normalized.IndexOf("#{");
                if (hashIndex > 0)
                {
                    normalized = normalized.Substring(0, hashIndex);
                }

                normalized = normalized.Replace('#', '\\');
                var keyPath = @"SYSTEM\CurrentControlSet\Enum\" + normalized + @"\Device Parameters";
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    var edid = key?.GetValue("EDID") as byte[];
                    return ParseSerial(edid);
                }
            }
            catch
            {
                return null;
            }
        }

        public static uint? ParseSerial(byte[] edid)
        {
            if (edid == null || edid.Length < 16)
            {
                return null;
            }

            // Bytes 12-15: serial number, little-endian.
            var serial = (uint)(edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));
            return serial == 0 ? (uint?)null : serial;
        }
    }
}
