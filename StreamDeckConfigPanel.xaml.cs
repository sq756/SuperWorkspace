using System.Windows;

namespace SuperWorkspace
{
    public partial class StreamDeckConfigPanel : System.Windows.Controls.UserControl
    {
        private MainWindow _main;

        public StreamDeckConfigPanel(MainWindow main)
        {
            InitializeComponent();
            _main = main;
        }

        private void Btn_CancelStreamDeckConfig_Click(object sender, RoutedEventArgs e)
        {
            _main.CloseOverlay();
        }

        private void Btn_LaunchStreamDeck_Click(object sender, RoutedEventArgs e)
        {
            _main.LaunchStreamDeck();
        }
    }
}