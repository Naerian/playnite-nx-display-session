using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace PlayniteDisplayManager.Hdr
{
    /// <summary>
    /// Local Playnite Features/Tags match for HDR policy 3. No network on the launch path.
    /// </summary>
    public static class HdrMetadataMatcher
    {
        public static readonly string[] DefaultMatchNames =
        {
            "HDR",
            "HDR10",
            "Dolby Vision",
            "Auto HDR",
            "HDR10+"
        };

        public static IReadOnlyList<string> NormalizeMatchNames(IEnumerable<string> names)
        {
            if (names == null)
            {
                return DefaultMatchNames.ToList();
            }

            var normalized = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return normalized.Count > 0 ? normalized : DefaultMatchNames.ToList();
        }

        public static bool GameIndicatesHdr(
            Game game,
            IEnumerable<string> matchNames,
            bool includeTags)
        {
            if (game == null)
            {
                return false;
            }

            var needles = new HashSet<string>(
                NormalizeMatchNames(matchNames),
                StringComparer.OrdinalIgnoreCase);

            if (MatchesAny(game.Features?.Select(f => f?.Name), needles))
            {
                return true;
            }

            if (includeTags && MatchesAny(game.Tags?.Select(t => t?.Name), needles))
            {
                return true;
            }

            return false;
        }

        public static bool NamesIndicateHdr(
            IEnumerable<string> featureOrTagNames,
            IEnumerable<string> matchNames)
        {
            var needles = new HashSet<string>(
                NormalizeMatchNames(matchNames),
                StringComparer.OrdinalIgnoreCase);
            return MatchesAny(featureOrTagNames, needles);
        }

        private static bool MatchesAny(IEnumerable<string> candidates, HashSet<string> needles)
        {
            if (candidates == null || needles == null || needles.Count == 0)
            {
                return false;
            }

            foreach (var raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (needles.Contains(raw.Trim()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
