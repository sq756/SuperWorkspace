using System.Collections.Generic;
using System.Windows;

namespace SuperWorkspace
{
    public partial class SingleWindowConfigPanel : System.Windows.Controls.UserControl
    {
        private MainWindow _main;

        public SingleWindowConfigPanel(MainWindow main, System.Windows.Controls.ItemCollection devices, int selectedDeviceIndex, List<MainWindow.WindowInfo> windows)
        {
            InitializeComponent();
            _main = main;
            
            foreach (var dev in devices) ComboSingleWindowTargetDevice.Items.Add(dev);
            ComboSingleWindowTargetDevice.SelectedIndex = selectedDeviceIndex;

            foreach (var win in windows) ComboWindowList.Items.Add(win);
            if (ComboWindowList.Items.Count > 0) ComboWindowList.SelectedIndex = 0;
        }

        private void Btn_CancelSingleWindowConfig_Click(object sender, RoutedEventArgs e)
        {
            _main.CloseOverlay();
        }

        private void Btn_LaunchSingleWindowMirror_Click(object sender, RoutedEventArgs e)
        {
            var targetWin = ComboWindowList.SelectedItem as MainWindow.WindowInfo;
            var targetDevice = ComboSingleWindowTargetDevice.SelectedItem as AdbDevice;
            _main.LaunchSingleWindowMirror(targetWin, targetDevice);
        }
    }
}