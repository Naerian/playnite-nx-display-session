using System;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Resolution;

namespace PlayniteDisplayManager.Profiles
{
    /// <summary>
    /// Effective session plan after Game > Platform > Default display profile + globals.
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

        public GameResolutionOverride ResolutionOverride { get; set; } =
            GameResolutionOverride.Inherit;

        public int? PreferredResolutionWidth { get; set; }

        public int? PreferredResolutionHeight { get; set; }

        public Guid? DisplayProfileId { get; set; }

        public string DisplayProfileName { get; set; }
    }

    public static class SessionProfileResolver
    {
        public static ResolvedSessionProfile Resolve(
            GameDisplayProfile gameProfile,
            GameDisplayProfile platformProfile,
            DisplayProfile defaultTopology,
            Func<Guid, DisplayProfile> topologyById)
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

            DisplayProfile topology = defaultTopology;
            if (layer?.DisplayProfileId != null && topologyById != null)
            {
                var linked = topologyById(layer.DisplayProfileId.Value);
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
                ResolutionOverride = topology?.ResolutionOverride
                    ?? GameResolutionOverride.Inherit,
                PreferredResolutionWidth = topology?.PreferredResolutionWidth,
                PreferredResolutionHeight = topology?.PreferredResolutionHeight,
                DisplayProfileId = topology?.Id,
                DisplayProfileName = topology?.Name
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

            if (layer.ResolutionOverride != GameResolutionOverride.Inherit)
            {
                resolved.ResolutionOverride = layer.ResolutionOverride;
                resolved.PreferredResolutionWidth = layer.PreferredResolutionWidth;
                resolved.PreferredResolutionHeight = layer.PreferredResolutionHeight;
            }

            return resolved;
        }
    }
}
