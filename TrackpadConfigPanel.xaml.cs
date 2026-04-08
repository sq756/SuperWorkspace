using System.Windows;

namespace SuperWorkspace
{
    public partial class TrackpadConfigPanel : System.Windows.Controls.UserControl
    {
        private MainWindow _main;

        public TrackpadConfigPanel(MainWindow main)
        {
            InitializeComponent();
            _main = main;
        }

        private void Btn_CancelTrackpadConfig_Click(object sender, RoutedEventArgs e)
        {
            _main.CloseOverlay();
        }

        private void Btn_LaunchVirtualInput_Click(object sender, RoutedEventArgs e)
        {
            _main.LaunchVirtualInput(SliderTrackpadSensitivity.Value);
        }
    }
}