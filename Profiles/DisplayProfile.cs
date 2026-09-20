using System;
using System.Runtime.Serialization;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Resolution;

namespace PlayniteDisplayManager.Profiles
{
    public enum MissingDisplayPolicy
    {
        /// <summary>Keep Windows primary; no MakePrimary.</summary>
        UseWindowsPrimary = 0,

        /// <summary>Use FallbackDisplayId when connected; otherwise Windows primary.</summary>
        UseFallbackDisplay = 1,

        /// <summary>Same as UseWindowsPrimary, with a stronger user notification.</summary>
        NotifyAndContinue = 2
    }

    /// <summary>
    /// Named display profile: play display, turn-off-others, missing fallback, optional HDR/Hz pins.
    /// </summary>
    [DataContract]
    public sealed class DisplayProfile
    {
        [DataMember(Name = "id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [DataMember(Name = "name")]
        public string Name { get; set; } = "Default";

        /// <summary>null/empty = keep Windows primary; otherwise stable display id.</summary>
        [DataMember(Name = "preferredPlayDisplayId")]
        public string PreferredPlayDisplayId { get; set; }

        [DataMember(Name = "turnOffOtherDisplays")]
        public bool TurnOffOtherDisplays { get; set; }

        [DataMember(Name = "missingDisplayPolicy")]
        public MissingDisplayPolicy MissingDisplayPolicy { get; set; } = MissingDisplayPolicy.UseWindowsPrimary;

        [DataMember(Name = "fallbackDisplayId")]
        public string FallbackDisplayId { get; set; }

        /// <summary>Inherit = use General global HDR policy.</summary>
        [DataMember(Name = "hdrOverride")]
        public GameHdrOverride HdrOverride { get; set; } = GameHdrOverride.Inherit;

        /// <summary>Inherit = use General global refresh policy.</summary>
        [DataMember(Name = "refreshRateOverride")]
        public GameRefreshRateOverride RefreshRateOverride { get; set; } = GameRefreshRateOverride.Inherit;

        [DataMember(Name = "preferredRefreshRateHz")]
        public double? PreferredRefreshRateHz { get; set; }

        /// <summary>Inherit = use General global resolution policy.</summary>
        [DataMember(Name = "resolutionOverride")]
        public GameResolutionOverride ResolutionOverride { get; set; } = GameResolutionOverride.Inherit;

        [DataMember(Name = "preferredResolutionWidth")]
        public int? PreferredResolutionWidth { get; set; }

        [DataMember(Name = "preferredResolutionHeight")]
        public int? PreferredResolutionHeight { get; set; }

        public DisplayProfile Clone()
        {
            return new DisplayProfile
            {
                Id = Id,
                Name = Name,
                PreferredPlayDisplayId = PreferredPlayDisplayId,
                TurnOffOtherDisplays = TurnOffOtherDisplays,
                MissingDisplayPolicy = MissingDisplayPolicy,
                FallbackDisplayId = FallbackDisplayId,
                HdrOverride = HdrOverride,
                RefreshRateOverride = RefreshRateOverride,
                PreferredRefreshRateHz = PreferredRefreshRateHz,
                ResolutionOverride = ResolutionOverride,
                PreferredResolutionWidth = PreferredResolutionWidth,
                PreferredResolutionHeight = PreferredResolutionHeight
            };
        }
    }
}
