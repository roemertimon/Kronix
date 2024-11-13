using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Kronix
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // Create and position the new window
            var dashboardWindow = new DashboardWindow
            {
                Top = this.Top,
                Left = this.Left,
                Width = this.Width,
                Height = this.Height,
                WindowStartupLocation = WindowStartupLocation.Manual // Position the new window in the same place as the old one
            };

            // Show the new window immediately
            dashboardWindow.Show();

            // Close the current window right after showing the new one
            this.Close();
        }








    }
}