using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace PlayniteDisplayManager.Theme
{
    internal static class SvgIconGeometryLoader
    {
        private static readonly Dictionary<string, string> Cache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string GetPathData(string fileName)
        {
            var safeName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return string.Empty;
            }

            string cached;
            if (Cache.TryGetValue(safeName, out cached))
            {
                return cached;
            }

            try
            {
                var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var path = Path.Combine(directory ?? string.Empty, "Icons", safeName);
                if (!File.Exists(path))
                {
                    Cache[safeName] = string.Empty;
                    return string.Empty;
                }

                var document = XDocument.Load(path);
                cached = string.Join(" ", document.Descendants()
                    .Where(a => !string.Equals((string)a.Attribute("stroke"), "none", StringComparison.OrdinalIgnoreCase))
                    .Select(GetGeometryData)
                    .Where(a => !string.IsNullOrWhiteSpace(a)));
            }
            catch
            {
                cached = string.Empty;
            }

            Cache[safeName] = cached;
            return cached;
        }

        private static string GetGeometryData(XElement element)
        {
            if (string.Equals(element.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase))
            {
                var data = (string)element.Attribute("d");
                return string.Equals((string)element.Attribute("fill-rule"), "evenodd", StringComparison.OrdinalIgnoreCase)
                    ? "F0 " + data
                    : data;
            }

            return string.Empty;
        }
    }
}
