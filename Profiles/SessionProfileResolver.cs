using System;
using PlayniteDisplayManager.Refresh;

namespace PlayniteDisplayManager.Profiles
{
    /// <summary>
    /// Effective session plan after Game > Platform > Default topology + globals.
    /// </summary>
    public sealed class ResolvedSessionProfile
    {
        public string Source { get; set; }

        /// <summary>null/empty = keep Windows primary; otherwise preferred display id.</summary>
        public string PreferredPlayDisplayId { get; set; }

        public bool TurnOffOtherDisplays { get; set; }

        public MissingDisplayPolicy MissingDisplayPolicy { get; set; } =
            MissingDisplayPolicy.UseWindowsPrimary;

        public string FallbackDisplayId { get; set; }

        public GameHdrOverride HdrOverride { get; set; } = GameHdrOverride.Inherit;

        public GameRefreshRateOverride RefreshRateOverride { get; set; } =
            GameRefreshRateOverride.Inherit;

        public double? PreferredRefreshRateHz { get; set; }

        public Guid? TopologyProfileId { get; set; }

        public string TopologyProfileName { get; set; }
    }

    public static class SessionProfileResolver
    {
        public static ResolvedSessionProfile Resolve(
            GameDisplayProfile gameProfile,
            GameDisplayProfile platformProfile,
            TopologyProfile defaultTopology,
            Func<Guid, TopologyProfile> topologyById)
        {
            var source = "global";
            GameDisplayProfile layer = null;

            if (gameProfile != null && !gameProfile.IsEmpty)
            {
                layer = gameProfile;
                source = "game";
            }
            else if (platformProfile != null && !platformProfile.IsEmpty)
            {
                layer = platformProfile;
                source = "platform";
            }

            TopologyProfile topology = defaultTopology;
            if (layer?.TopologyProfileId != null && topologyById != null)
            {
                var linked = topologyById(layer.TopologyProfileId.Value);
                if (linked != null)
                {
                    topology = linked;
                }
            }

            var resolved = new ResolvedSessionProfile
            {
                Source = source,
                PreferredPlayDisplayId = topology?.PreferredPlayDisplayId,
                TurnOffOtherDisplays = topology?.TurnOffOtherDisplays ?? false,
                MissingDisplayPolicy = topology?.MissingDisplayPolicy
                    ?? MissingDisplayPolicy.UseWindowsPrimary,
                FallbackDisplayId = topology?.FallbackDisplayId,
                HdrOverride = topology?.HdrOverride ?? GameHdrOverride.Inherit,
                RefreshRateOverride = topology?.RefreshRateOverride
                    ?? GameRefreshRateOverride.Inherit,
                PreferredRefreshRateHz = topology?.PreferredRefreshRateHz,
                TopologyProfileId = topology?.Id,
                TopologyProfileName = topology?.Name
            };

            if (layer == null)
            {
                return resolved;
            }

            if (layer.HasPlayDisplayOverride)
            {
                resolved.PreferredPlayDisplayId = layer.PreferredPlayDisplayId;
            }

            if (layer.HdrOverride != GameHdrOverride.Inherit)
            {
                resolved.HdrOverride = layer.HdrOverride;
            }

            if (layer.RefreshRateOverride != GameRefreshRateOverride.Inherit)
            {
                resolved.RefreshRateOverride = layer.RefreshRateOverride;
                resolved.PreferredRefreshRateHz = layer.PreferredRefreshRateHz;
            }

            return resolved;
        }
    }
}
