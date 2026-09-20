using System;
using PlayniteDisplayManager.Refresh;

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

        /// <summary>null = inherit; empty = Windows primary; otherwise display id.</summary>
        public string PreferredPlayDisplayId { get; set; }

        public GameDisplayProfile ToProfile()
        {
            return new GameDisplayProfile
            {
                HdrOverride = HdrOverride,
                RefreshRateOverride = RefreshRateOverride,
                PreferredRefreshRateHz = PreferredRefreshRateHz,
                PreferredPlayDisplayId = PreferredPlayDisplayId
            };
        }
    }
}
