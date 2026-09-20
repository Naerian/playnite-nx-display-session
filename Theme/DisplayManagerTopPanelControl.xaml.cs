using System.Windows.Media;
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
            ApplyIconGeometry();
            Loaded += (_, __) =>
            {
                ApplyIconGeometry();
                plugin.Theme.Refresh();
            };
        }

        private void ApplyIconGeometry()
        {
            if (IconPath == null)
            {
                return;
            }

            var data = SvgIconGeometryLoader.GetPathData("display-settings.svg");
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            try
            {
                IconPath.Data = Geometry.Parse(data);
            }
            catch
            {
                // Keep empty path if SVG parse fails.
            }
        }
    }
}
