using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace PlayniteDisplayManager.Displays
{
    /// <summary>
    /// Reads EDID from the monitor device registry (serial + discrete resolutions).
    /// </summary>
    public static class DisplayEdidReader
    {
        public static uint? TryReadSerial(string monitorDevicePath)
        {
            return ParseSerial(TryReadBytes(monitorDevicePath));
        }

        /// <summary>
        /// Discrete WxH pairs advertised by the monitor EDID (base + extension blocks).
        /// Returns null when EDID is missing/unreadable so callers can avoid false "custom" labels.
        /// </summary>
        public static ISet<long> TryReadResolutionKeys(string monitorDevicePath)
        {
            var edid = TryReadBytes(monitorDevicePath);
            if (edid == null || edid.Length < 128)
            {
                return null;
            }

            var keys = new HashSet<long>();
            ParseBaseBlock(edid, keys);

            var extensionCount = edid[126];
            for (var e = 0; e < extensionCount; e++)
            {
                var offset = 128 * (e + 1);
                if (edid.Length < offset + 128)
                {
                    break;
                }

                ParseExtensionBlock(edid, offset, keys);
            }

            return keys.Count > 0 ? keys : null;
        }

        public static long ToKey(int width, int height)
        {
            return ((long)width << 32) | (uint)height;
        }

        public static byte[] TryReadBytes(string monitorDevicePath)
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
                    return key?.GetValue("EDID") as byte[];
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

        private static void ParseBaseBlock(byte[] edid, ISet<long> keys)
        {
            // Established Timings I (byte 35) — VESA DMT bit map (WxH only).
            AddEstablishedBit(edid[35], 1 << 7, 720, 400, keys);
            AddEstablishedBit(edid[35], 1 << 6, 720, 400, keys);
            AddEstablishedBit(edid[35], 1 << 5, 640, 480, keys);
            AddEstablishedBit(edid[35], 1 << 4, 640, 480, keys);
            AddEstablishedBit(edid[35], 1 << 3, 640, 480, keys);
            AddEstablishedBit(edid[35], 1 << 2, 640, 480, keys);
            AddEstablishedBit(edid[35], 1 << 1, 800, 600, keys);
            AddEstablishedBit(edid[35], 1 << 0, 800, 600, keys);
            // Established Timings II (byte 36)
            AddEstablishedBit(edid[36], 1 << 7, 800, 600, keys);
            AddEstablishedBit(edid[36], 1 << 6, 800, 600, keys);
            AddEstablishedBit(edid[36], 1 << 5, 832, 624, keys);
            AddEstablishedBit(edid[36], 1 << 4, 1024, 768, keys);
            AddEstablishedBit(edid[36], 1 << 3, 1024, 768, keys);
            AddEstablishedBit(edid[36], 1 << 2, 1024, 768, keys);
            AddEstablishedBit(edid[36], 1 << 1, 1024, 768, keys);
            AddEstablishedBit(edid[36], 1 << 0, 1280, 1024, keys);
            // Manufacturer timings (byte 37 bit 7): 1152×870 @75
            if ((edid[37] & 0x80) != 0)
            {
                keys.Add(ToKey(1152, 870));
            }

            // Standard timings (8 × 2 bytes at 38..53)
            for (var i = 38; i <= 52; i += 2)
            {
                if (edid[i] == 0x01 && edid[i + 1] == 0x01)
                {
                    continue;
                }

                var h = (edid[i] + 31) * 8;
                var aspectBits = (edid[i + 1] >> 6) & 0x3;
                double ratio;
                switch (aspectBits)
                {
                    case 0: ratio = 16.0 / 10.0; break;
                    case 1: ratio = 4.0 / 3.0; break;
                    case 2: ratio = 5.0 / 4.0; break;
                    default: ratio = 16.0 / 9.0; break;
                }

                var v = (int)Math.Round(h / ratio);
                if (h >= 640 && v >= 480)
                {
                    keys.Add(ToKey(h, v));
                }
            }

            // 18-byte descriptors at 54, 72, 90, 108
            for (var offset = 54; offset <= 108; offset += 18)
            {
                ParseDescriptor(edid, offset, keys);
            }
        }

        private static void AddEstablishedBit(byte bits, int mask, int width, int height, ISet<long> keys)
        {
            if ((bits & mask) != 0)
            {
                keys.Add(ToKey(width, height));
            }
        }

        private static void ParseDescriptor(byte[] edid, int offset, ISet<long> keys)
        {
            if (offset + 17 >= edid.Length)
            {
                return;
            }

            // Detailed timing if pixel clock != 0
            if (edid[offset] != 0 || edid[offset + 1] != 0)
            {
                var hActive = edid[offset + 2] | ((edid[offset + 4] & 0xF0) << 4);
                var vActive = edid[offset + 5] | ((edid[offset + 7] & 0xF0) << 4);
                if (hActive >= 640 && vActive >= 480)
                {
                    keys.Add(ToKey(hActive, vActive));
                }

                return;
            }

            // Display descriptor tag at offset+3
            var tag = edid[offset + 3];
            // 0xFA = standard timing identifiers additional
            if (tag == 0xFA)
            {
                for (var i = 5; i <= 16; i += 2)
                {
                    if (offset + i + 1 >= edid.Length)
                    {
                        break;
                    }

                    if (edid[offset + i] == 0x01 && edid[offset + i + 1] == 0x01)
                    {
                        continue;
                    }

                    var h = (edid[offset + i] + 31) * 8;
                    var aspectBits = (edid[offset + i + 1] >> 6) & 0x3;
                    double ratio;
                    switch (aspectBits)
                    {
                        case 0: ratio = 16.0 / 10.0; break;
                        case 1: ratio = 4.0 / 3.0; break;
                        case 2: ratio = 5.0 / 4.0; break;
                        default: ratio = 16.0 / 9.0; break;
                    }

                    var v = (int)Math.Round(h / ratio);
                    if (h >= 640 && v >= 480)
                    {
                        keys.Add(ToKey(h, v));
                    }
                }
            }
        }

        private static void ParseExtensionBlock(byte[] edid, int offset, ISet<long> keys)
        {
            var tag = edid[offset];
            // CTA-861 / CEA extension
            if (tag == 0x02)
            {
                var dtdStart = edid[offset + 2];
                if (dtdStart >= 4)
                {
                    for (var d = offset + dtdStart; d + 17 < offset + 128; d += 18)
                    {
                        if (edid[d] == 0 && edid[d + 1] == 0)
                        {
                            break;
                        }

                        ParseDescriptor(edid, d, keys);
                    }
                }

                // Short video descriptors in data blocks (bytes 4 .. dtdStart-1)
                var end = offset + Math.Min(dtdStart > 0 ? dtdStart : 4, 127);
                var i = offset + 4;
                while (i < end)
                {
                    var header = edid[i];
                    var blockType = (header >> 5) & 0x7;
                    var length = header & 0x1F;
                    if (i + 1 + length > offset + 128)
                    {
                        break;
                    }

                    if (blockType == 2) // Video Data Block
                    {
                        for (var v = 0; v < length; v++)
                        {
                            var vic = edid[i + 1 + v] & 0x7F;
                            AddVic(vic, keys);
                        }
                    }

                    i += 1 + length;
                }

                return;
            }

            // DisplayID or other: try scanning 18-byte detailed timings
            for (var d = offset + 5; d + 17 < offset + 128; d += 18)
            {
                if (edid[d] != 0 || edid[d + 1] != 0)
                {
                    ParseDescriptor(edid, d, keys);
                }
            }
        }

        private static void AddVic(int vic, ISet<long> keys)
        {
            // Common CTA-861 VICs → active pixels (not exhaustive; covers typical desktop).
            switch (vic)
            {
                case 1: case 2: case 3: keys.Add(ToKey(640, 480)); break;
                case 4: case 5: keys.Add(ToKey(1280, 720)); break;
                case 16: case 31: case 32: case 33: case 34: keys.Add(ToKey(1920, 1080)); break;
                case 17: case 18: keys.Add(ToKey(720, 576)); break;
                case 19: case 20: keys.Add(ToKey(1280, 720)); break;
                case 39: case 40: case 41: case 42: case 43: case 44: case 45:
                    keys.Add(ToKey(1920, 1080));
                    break;
                case 61: case 62: keys.Add(ToKey(1920, 1080)); break;
                case 93: case 94: case 95: case 96: case 97:
                    keys.Add(ToKey(3840, 2160));
                    break;
                case 98: case 99: case 100: case 101:
                    keys.Add(ToKey(4096, 2160));
                    break;
                case 102: case 103: case 104: case 105: case 106: case 107:
                    keys.Add(ToKey(3840, 2160));
                    break;
            }
        }
    }
}
