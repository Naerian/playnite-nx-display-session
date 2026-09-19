using Playnite.SDK.Controls;

namespace PlayniteDisplayManager.Theme
{
    public partial class DisplayManagerTopPanelControl : PluginUserControl
    {
        private readonly PlayniteDisplayManagerPlugin plugin;

        public DisplayManagerTopPanelControl(PlayniteDisplayManagerPlugin sourcePlugin)
        {
            plugin = sourcePlugin;
            InitializeComponent();
            DataContext = plugin.Theme;
            Loaded += (_, __) => plugin.Theme.Refresh();
        }
    }
}
