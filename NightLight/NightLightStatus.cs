namespace PlayniteDisplayManager.NightLight
{
    /// <summary>
    /// Intended Night Light session policy. v1 only persists DoNotTouch —
    /// Windows has no public deterministic Night Light API across Win10+Win11.
    /// </summary>
    public enum NightLightPolicy
    {
        /// <summary>Leave Night Light alone (only shipped behaviour in v1).</summary>
        DoNotTouch = 0

        // ForceOffDuringGame = 1  — reserved; undocumented CloudStore writes are not shipped.
    }

    /// <summary>
    /// Honest status for Overview. Does not read undocumented CloudStore blobs
    /// as if they were a supported API.
    /// </summary>
    public static class NightLightStatus
    {
        public const string CapabilityNote =
            "Windows does not expose a supported Night Light read/write API that is deterministic across Windows 10 and Windows 11. " +
            "Undocumented CloudStore registry formats change between builds and can leave Night Light broken until logoff. " +
            "Display Manager therefore does not control Night Light in this version.";

        public static string GetOverviewKey() => "LOCDisplayManager_OverviewNightLightCut";
    }
}
