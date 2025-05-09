using System.Windows;

namespace MusicZeroV2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Position the window at the right edge, centered vertically
            Left = SystemParameters.WorkArea.Width - 10;
            Top = (SystemParameters.WorkArea.Height - 100) / 2;
            
            // Ensure the window is visible
            Show();
            Activate();
        }
    }
} 