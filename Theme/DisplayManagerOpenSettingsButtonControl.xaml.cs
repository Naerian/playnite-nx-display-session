using Playnite.SDK.Controls;

namespace PlayniteDisplayManager.Theme
{
    public partial class DisplayManagerOpenSettingsButtonControl : PluginUserControl
    {
        private readonly PlayniteDisplayManagerPlugin plugin;

        public DisplayManagerOpenSettingsButtonControl(PlayniteDisplayManagerPlugin sourcePlugin)
        {
            plugin = sourcePlugin;
            InitializeComponent();
            DataContext = plugin.Theme;
            Loaded += (_, __) => plugin.Theme.Refresh();
        }
    }
}
