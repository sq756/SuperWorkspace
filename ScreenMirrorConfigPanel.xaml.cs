using System.Windows;

namespace SuperWorkspace
{
    public partial class ScreenMirrorConfigPanel : System.Windows.Controls.UserControl
    {
        private MainWindow _main;

        public ScreenMirrorConfigPanel(MainWindow main)
        {
            InitializeComponent();
            _main = main;
        }

        private void Btn_CancelConfig_Click(object sender, RoutedEventArgs e)
        {
            _main.CloseOverlay();
        }

        private void Btn_LaunchMirror_Click(object sender, RoutedEventArgs e)
        {
            _main.LaunchScreenMirror(
                ComboResolution.SelectedIndex,
                (int)SliderFps.Value,
                ComboCodec.SelectedIndex,
                ComboBitrate.SelectedIndex,
                ComboKeyboard.SelectedIndex,
                CheckScreenOff.IsChecked == true,
                CheckAudio.IsChecked == true
            );
        }
    }
}