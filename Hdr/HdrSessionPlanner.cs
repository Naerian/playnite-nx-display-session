using System;
using Playnite.SDK.Models;
using PlayniteDisplayManager.Profiles;

namespace PlayniteDisplayManager.Hdr
{
    public enum HdrSessionAction
    {
        /// <summary>Do not manage HDR for this launch.</summary>
        None = 0,

        /// <summary>Write HDR on (primary) at start; write off on exit.</summary>
        Enable = 1,

        /// <summary>Write HDR off at start; write off on exit.</summary>
        Disable = 2
    }

    public sealed class HdrSessionPlan
    {
        public HdrSessionAction Action { get; set; }

        public GameHdrOverride EffectiveOverride { get; set; }

        public bool MetadataMatched { get; set; }

        public string Reason { get; set; }
    }

    /// <summary>
    /// Resolves per-game HDR override over the global policy. Override always wins.
    /// </summary>
    public static class HdrSessionPlanner
    {
        public static HdrSessionPlan Plan(
            Game game,
            GlobalHdrPolicy globalPolicy,
            GameDisplayProfile profile,
            System.Collections.Generic.IEnumerable<string> metadataMatchNames,
            bool includeTagsInMetadata)
        {
            var hdrOverride = profile?.HdrOverride ?? GameHdrOverride.Inherit;
            var metadataMatched = HdrMetadataMatcher.GameIndicatesHdr(
                game, metadataMatchNames, includeTagsInMetadata);

            switch (hdrOverride)
            {
                case GameHdrOverride.ForceOn:
                    return new HdrSessionPlan
                    {
                        Action = HdrSessionAction.Enable,
                        EffectiveOverride = hdrOverride,
                        MetadataMatched = metadataMatched,
                        Reason = "per-game force on"
                    };
                case GameHdrOverride.ForceOff:
                    return new HdrSessionPlan
                    {
                        Action = HdrSessionAction.Disable,
                        EffectiveOverride = hdrOverride,
                        MetadataMatched = metadataMatched,
                        Reason = "per-game force off (SDR)"
                    };
                case GameHdrOverride.DoNotTouch:
                    return new HdrSessionPlan
                    {
                        Action = HdrSessionAction.None,
                        EffectiveOverride = hdrOverride,
                        MetadataMatched = metadataMatched,
                        Reason = "per-game do not touch"
                    };
            }

            switch (globalPolicy)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    return new HdrSessionPlan
                    {
                        Action = HdrSessionAction.Enable,
                        EffectiveOverride = GameHdrOverride.Inherit,
                        MetadataMatched = metadataMatched,
                        Reason = "global any-game"
                    };
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    if (metadataMatched)
                    {
                        return new HdrSessionPlan
                        {
                            Action = HdrSessionAction.Enable,
                            EffectiveOverride = GameHdrOverride.Inherit,
                            MetadataMatched = true,
                            Reason = "global metadata match"
                        };
                    }

                    return new HdrSessionPlan
                    {
                        Action = HdrSessionAction.None,
                        EffectiveOverride = GameHdrOverride.Inherit,
                        MetadataMatched = false,
                        Reason = "global metadata: no match"
                    };
                default:
                    return new HdrSessionPlan
                    {
                        Action = HdrSessionAction.None,
                        EffectiveOverride = GameHdrOverride.Inherit,
                        MetadataMatched = metadataMatched,
                        Reason = "global do not manage"
                    };
            }
        }
    }
}
