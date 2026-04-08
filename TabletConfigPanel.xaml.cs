using System.Windows;

namespace SuperWorkspace
{
    public partial class TabletConfigPanel : System.Windows.Controls.UserControl
    {
        private MainWindow _main;

        public TabletConfigPanel(MainWindow main)
        {
            InitializeComponent();
            _main = main;
        }

        private void Btn_CancelTabletConfig_Click(object sender, RoutedEventArgs e)
        {
            _main.CloseOverlay();
        }

        private void Btn_LaunchTablet_Click(object sender, RoutedEventArgs e)
        {
            _main.LaunchTablet(SliderPressureCurve.Value, CheckPalmReject.IsChecked == true);
        }
    }
}