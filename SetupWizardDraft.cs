using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Refresh;

namespace PlayniteDisplayManager
{
    public sealed class SetupWizardDraft
    {
        public bool SetupWizardCompleted { get; set; }

        /// <summary>Null/empty = keep Windows primary. Otherwise a display Id.</summary>
        public string PreferredPlayDisplayId { get; set; }

        public bool TurnOffOtherDisplays { get; set; }

        public GlobalHdrPolicy GlobalHdrPolicy { get; set; } = GlobalHdrPolicy.DoNotManage;

        public RefreshRatePolicy GlobalRefreshRatePolicy { get; set; } = RefreshRatePolicy.Native;

        public double? PreferredRefreshRateHz { get; set; }

        /// <summary>When finishing, clear EnableSystemHdr on all games (one-shot migration).</summary>
        public bool ClearNativeHdrFlags { get; set; } = true;
    }
}
