using System;
using System.Runtime.Serialization;
using PlayniteDisplayManager.Refresh;

namespace PlayniteDisplayManager.Profiles
{
    public enum GameHdrOverride
    {
        /// <summary>Use the global HDR policy.</summary>
        Inherit = 0,

        /// <summary>Write HDR on for this game regardless of global policy.</summary>
        ForceOn = 1,

        /// <summary>Write HDR off (SDR) for this game — the gap native Playnite lacks.</summary>
        ForceOff = 2,

        /// <summary>Leave HDR alone for this game.</summary>
        DoNotTouch = 3
    }

    /// <summary>
    /// Per-game or per-platform display profile stored in plugin user data.
    /// </summary>
    [DataContract]
    public sealed class GameDisplayProfile
    {
        [DataMember(Name = "hdrOverride")]
        public GameHdrOverride HdrOverride { get; set; } = GameHdrOverride.Inherit;

        [DataMember(Name = "refreshRateOverride")]
        public GameRefreshRateOverride RefreshRateOverride { get; set; } = GameRefreshRateOverride.Inherit;

        /// <summary>Target Hz when RefreshRateOverride is ExactHz (or legacy Prefer60/Prefer120).</summary>
        [DataMember(Name = "preferredRefreshRateHz")]
        public double? PreferredRefreshRateHz { get; set; }

        /// <summary>
        /// Per-game play display. null = inherit topology/global;
        /// empty string = keep Windows primary; otherwise a stable display id.
        /// </summary>
        [DataMember(Name = "preferredPlayDisplayId")]
        public string PreferredPlayDisplayId { get; set; }

        /// <summary>Optional named topology profile to apply before field-level overrides.</summary>
        [DataMember(Name = "topologyProfileId")]
        public Guid? TopologyProfileId { get; set; }

        /// <summary>True when PreferredPlayDisplayId is set (including empty = Windows primary).</summary>
        public bool HasPlayDisplayOverride => PreferredPlayDisplayId != null;

        public bool IsEmpty =>
            HdrOverride == GameHdrOverride.Inherit &&
            RefreshRateOverride == GameRefreshRateOverride.Inherit &&
            PreferredPlayDisplayId == null &&
            TopologyProfileId == null;

        public GameDisplayProfile Clone()
        {
            return new GameDisplayProfile
            {
                HdrOverride = HdrOverride,
                RefreshRateOverride = RefreshRateOverride,
                PreferredRefreshRateHz = PreferredRefreshRateHz,
                PreferredPlayDisplayId = PreferredPlayDisplayId,
                TopologyProfileId = TopologyProfileId
            };
        }
    }
}
