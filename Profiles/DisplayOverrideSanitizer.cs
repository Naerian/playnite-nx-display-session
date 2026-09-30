using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Refresh;
using PlayniteDisplayManager.Resolution;

namespace PlayniteDisplayManager.Profiles
{
    public sealed class DisplayOverrideSanitizeResult
    {
        public bool ResetResolution { get; set; }

        public bool ResetRefreshRate { get; set; }

        public bool ResetHdr { get; set; }

        public string ResetResolutionLabel { get; set; }

        public double? ResetRefreshRateHz { get; set; }

        public bool Changed => ResetResolution || ResetRefreshRate || ResetHdr;

        public void Merge(DisplayOverrideSanitizeResult other)
        {
            if (other == null || !other.Changed)
            {
                return;
            }

            if (other.ResetResolution)
            {
                ResetResolution = true;
                if (string.IsNullOrWhiteSpace(ResetResolutionLabel))
                {
                    ResetResolutionLabel = other.ResetResolutionLabel;
                }
            }

            if (other.ResetRefreshRate)
            {
                ResetRefreshRate = true;
                if (!ResetRefreshRateHz.HasValue)
                {
                    ResetRefreshRateHz = other.ResetRefreshRateHz;
                }
            }

            if (other.ResetHdr)
            {
                ResetHdr = true;
            }
        }
    }

    /// <summary>
    /// When the play display changes, Exact resolution / Exact Hz / Force HDR On that the
    /// new target cannot satisfy fall back to Inherit (use global / leave alone).
    /// </summary>
    public static class DisplayOverrideSanitizer
    {
        public static DisplayOverrideSanitizeResult SanitizeGameProfile(
            GameDisplayProfile profile,
            DisplayInfo target,
            ResolutionService resolutions,
            RefreshRateService refreshRates,
            Func<DisplayInfo, bool> isHdrSupported)
        {
            var result = new DisplayOverrideSanitizeResult();
            if (profile == null || target == null)
            {
                return result;
            }

            if (profile.ResolutionOverride == GameResolutionOverride.Exact
                && profile.PreferredResolutionWidth > 0
                && profile.PreferredResolutionHeight > 0)
            {
                var width = profile.PreferredResolutionWidth.Value;
                var height = profile.PreferredResolutionHeight.Value;
                var modes = resolutions?.GetAvailableModes(target);
                var ok = modes != null && modes.Any(m => m.Width == width && m.Height == height);
                if (!ok)
                {
                    result.ResetResolution = true;
                    result.ResetResolutionLabel = width + " × " + height;
                    profile.ResolutionOverride = GameResolutionOverride.Inherit;
                    profile.PreferredResolutionWidth = null;
                    profile.PreferredResolutionHeight = null;
                }
            }

            if (profile.RefreshRateOverride == GameRefreshRateOverride.ExactHz
                && profile.PreferredRefreshRateHz.HasValue)
            {
                var preferred = profile.PreferredRefreshRateHz.Value;
                var rates = refreshRates?.GetAvailableRates(target);
                var ok = rates != null && rates.Any(r => Math.Abs(r - preferred) < 0.6);
                if (!ok)
                {
                    result.ResetRefreshRate = true;
                    result.ResetRefreshRateHz = preferred;
                    profile.RefreshRateOverride = GameRefreshRateOverride.Inherit;
                    profile.PreferredRefreshRateHz = null;
                }
            }

            if (profile.HdrOverride == GameHdrOverride.ForceOn
                && isHdrSupported != null
                && !isHdrSupported(target))
            {
                result.ResetHdr = true;
                profile.HdrOverride = GameHdrOverride.Inherit;
            }

            return result;
        }

        public static DisplayOverrideSanitizeResult SanitizeGameProfileEntry(
            GameDisplayProfileEntry profile,
            DisplayInfo target,
            ResolutionService resolutions,
            RefreshRateService refreshRates,
            Func<DisplayInfo, bool> isHdrSupported)
        {
            if (profile == null)
            {
                return new DisplayOverrideSanitizeResult();
            }

            var bridge = new GameDisplayProfile
            {
                HdrOverride = profile.HdrOverride,
                RefreshRateOverride = profile.RefreshRateOverride,
                PreferredRefreshRateHz = profile.PreferredRefreshRateHz,
                ResolutionOverride = profile.ResolutionOverride,
                PreferredResolutionWidth = profile.PreferredResolutionWidth,
                PreferredResolutionHeight = profile.PreferredResolutionHeight,
                PreferredPlayDisplayId = profile.PreferredPlayDisplayId
            };

            var result = SanitizeGameProfile(bridge, target, resolutions, refreshRates, isHdrSupported);
            if (!result.Changed)
            {
                return result;
            }

            profile.HdrOverride = bridge.HdrOverride;
            profile.RefreshRateOverride = bridge.RefreshRateOverride;
            profile.PreferredRefreshRateHz = bridge.PreferredRefreshRateHz;
            profile.ResolutionOverride = bridge.ResolutionOverride;
            profile.PreferredResolutionWidth = bridge.PreferredResolutionWidth;
            profile.PreferredResolutionHeight = bridge.PreferredResolutionHeight;
            return result;
        }

        public static DisplayOverrideSanitizeResult SanitizeGlobalSettings(
            int? preferredWidth,
            int? preferredHeight,
            ResolutionPolicy resolutionPolicy,
            double? preferredHz,
            RefreshRatePolicy refreshPolicy,
            DisplayInfo target,
            ResolutionService resolutions,
            RefreshRateService refreshRates,
            out ResolutionPolicy newResolutionPolicy,
            out int? newPreferredWidth,
            out int? newPreferredHeight,
            out RefreshRatePolicy newRefreshPolicy,
            out double? newPreferredHz)
        {
            newResolutionPolicy = resolutionPolicy;
            newPreferredWidth = preferredWidth;
            newPreferredHeight = preferredHeight;
            newRefreshPolicy = refreshPolicy;
            newPreferredHz = preferredHz;

            var result = new DisplayOverrideSanitizeResult();
            if (target == null)
            {
                return result;
            }

            if (resolutionPolicy == ResolutionPolicy.Exact
                && preferredWidth > 0
                && preferredHeight > 0)
            {
                var modes = resolutions?.GetAvailableModes(target);
                var ok = modes != null && modes.Any(m =>
                    m.Width == preferredWidth.Value && m.Height == preferredHeight.Value);
                if (!ok)
                {
                    result.ResetResolution = true;
                    result.ResetResolutionLabel = preferredWidth.Value + " × " + preferredHeight.Value;
                    newResolutionPolicy = ResolutionPolicy.Native;
                    newPreferredWidth = null;
                    newPreferredHeight = null;
                }
            }

            if (refreshPolicy == RefreshRatePolicy.ExactHz && preferredHz.HasValue)
            {
                var rates = refreshRates?.GetAvailableRates(target);
                var ok = rates != null && rates.Any(r => Math.Abs(r - preferredHz.Value) < 0.6);
                if (!ok)
                {
                    result.ResetRefreshRate = true;
                    result.ResetRefreshRateHz = preferredHz;
                    newRefreshPolicy = RefreshRatePolicy.Native;
                    newPreferredHz = null;
                }
            }

            return result;
        }
    }
}
