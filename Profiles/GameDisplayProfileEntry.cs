using System;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Resolution;

namespace PlayniteDisplayManager.Profiles
{
    public sealed class GameDisplayProfileEntry
    {
        public Guid GameId { get; set; }

        public string GameName { get; set; }

        public string GameImagePath { get; set; }

        public GameHdrOverride HdrOverride { get; set; } = GameHdrOverride.Inherit;

        public GameRefreshRateOverride RefreshRateOverride { get; set; } = GameRefreshRateOverride.Inherit;

        public double? PreferredRefreshRateHz { get; set; }

        public GameResolutionOverride ResolutionOverride { get; set; } = GameResolutionOverride.Inherit;

        public int? PreferredResolutionWidth { get; set; }

        public int? PreferredResolutionHeight { get; set; }

        /// <summary>null = inherit; empty = Windows primary; otherwise display id.</summary>
        public string PreferredPlayDisplayId { get; set; }

        public Guid? DisplayProfileId { get; set; }

        public GameDisplayProfile ToProfile()
        {
            return new GameDisplayProfile
            {
                HdrOverride = HdrOverride,
                RefreshRateOverride = RefreshRateOverride,
                PreferredRefreshRateHz = PreferredRefreshRateHz,
                ResolutionOverride = ResolutionOverride,
                PreferredResolutionWidth = PreferredResolutionWidth,
                PreferredResolutionHeight = PreferredResolutionHeight,
                PreferredPlayDisplayId = PreferredPlayDisplayId,
                DisplayProfileId = DisplayProfileId
            };
        }
    }

    public sealed class PlatformProfileEntry
    {
        public Guid PlatformId { get; set; }

        public string PlatformName { get; set; }

        public GameHdrOverride HdrOverride { get; set; } = GameHdrOverride.Inherit;

        public GameRefreshRateOverride RefreshRateOverride { get; set; } = GameRefreshRateOverride.Inherit;

        public double? PreferredRefreshRateHz { get; set; }

        public GameResolutionOverride ResolutionOverride { get; set; } = GameResolutionOverride.Inherit;

        public int? PreferredResolutionWidth { get; set; }

        public int? PreferredResolutionHeight { get; set; }

        public string PreferredPlayDisplayId { get; set; }

        public Guid? DisplayProfileId { get; set; }

        public GameDisplayProfile ToProfile()
        {
            return new GameDisplayProfile
            {
                HdrOverride = HdrOverride,
                RefreshRateOverride = RefreshRateOverride,
                PreferredRefreshRateHz = PreferredRefreshRateHz,
                ResolutionOverride = ResolutionOverride,
                PreferredResolutionWidth = PreferredResolutionWidth,
                PreferredResolutionHeight = PreferredResolutionHeight,
                PreferredPlayDisplayId = PreferredPlayDisplayId,
                DisplayProfileId = DisplayProfileId
            };
        }
    }
}
