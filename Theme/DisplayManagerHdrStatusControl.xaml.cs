using Playnite.SDK.Controls;

namespace PlayniteDisplayManager.Theme
{
    public partial class DisplayManagerHdrStatusControl : PluginUserControl
    {
        private readonly PlayniteDisplayManagerPlugin plugin;

        public DisplayManagerHdrStatusControl(PlayniteDisplayManagerPlugin sourcePlugin)
        {
            plugin = sourcePlugin;
            InitializeComponent();
            DataContext = plugin.Theme;
            Loaded += (_, __) => plugin.Theme.Refresh();
        }
    }
}
