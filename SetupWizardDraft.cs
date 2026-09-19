namespace PlayniteDisplayManager
{
    public sealed class SetupWizardDraft
    {
        public bool SetupWizardCompleted { get; set; }

        /// <summary>When finishing, clear EnableSystemHdr on all games (one-shot migration).</summary>
        public bool ClearNativeHdrFlags { get; set; } = true;
    }
}
