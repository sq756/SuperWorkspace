using System.Windows;
using System.Windows.Controls;

namespace SuperWorkspace
{
    public partial class CameraConfigPanel : System.Windows.Controls.UserControl
    {
        private MainWindow _main;

        public CameraConfigPanel(MainWindow main)
        {
            InitializeComponent();
            _main = main;
        }

        private void Btn_CancelCameraConfig_Click(object sender, RoutedEventArgs e)
        {
            _main.CloseOverlay();
        }

        private void Btn_InstallDriver_Click(object sender, RoutedEventArgs e)
        {
            _main.InstallVirtualCameraDriver();
        }

        private void Btn_LaunchCamera_Click(object sender, RoutedEventArgs e)
        {
            _main.LaunchCamera(ComboCameraSource.SelectedIndex, ComboCameraRes.SelectedIndex, CheckEnableMic.IsChecked == true, CheckCameraScreenOff.IsChecked == true);
        }
    }
}