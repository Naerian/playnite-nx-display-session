using System.Windows;
using Playnite.SDK.Controls;

namespace PlayniteDisplayManager.Theme
{
    public partial class DisplayManagerDisplayListControl : PluginUserControl
    {
        private readonly PlayniteDisplayManagerPlugin plugin;

        public DisplayManagerDisplayListControl(PlayniteDisplayManagerPlugin sourcePlugin)
        {
            plugin = sourcePlugin;
            InitializeComponent();
            DataContext = plugin.Theme;
            Loaded += (_, __) => plugin.Theme.Refresh();
        }
    }
}
