namespace PlayniteDisplayManager.Displays
{
    public sealed class DisplayTopologyRequest
    {
        /// <summary>Stable display identity (EDID/connector). Null/empty = do not change topology target.</summary>
        public string TargetDisplayId { get; set; }

        public bool MakePrimary { get; set; }

        /// <summary>Opt-in: deactivate every active path except the target. Never allowed when it would leave zero displays.</summary>
        public bool TurnOffOtherDisplays { get; set; }
    }

    public sealed class DisplayTopologyApplyResult
    {
        public bool Success { get; set; }

        public string Error { get; set; }

        public DisplaySnapshot BeforeSnapshot { get; set; }

        public string Message { get; set; }
    }
}
