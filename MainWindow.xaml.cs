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
using Kronix.Database;

namespace Kronix
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    
    public partial class MainWindow : Window
    {
        private readonly DatabaseHelper _dbHelper;
        
        public MainWindow()
        {
            InitializeComponent();
    
            _dbHelper = new DatabaseHelper();
    
            // Test the database connection by initializing the DatabaseHelper
            try
            {
                if (!_dbHelper.IsDatabaseInitialized())
                {
                    throw new Exception("Datenbank Verbindung fehlgeschlagen!");
                }
                StartButton.Content = "Start"; // Update button text
                StartButton.IsEnabled = true;  // Enable the button if connection succeeds
                Console.WriteLine("Database connected successfully.");
            }
            catch (Exception ex)
            {
                StartButton.Content = ex.Message;
                Console.WriteLine("Failed to connect to the database: " + ex.Message);
            }
        }

        
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dbHelper.IsDatabaseInitialized())
            {
                // Create and position the new window
                var dashboardWindow = new DashboardWindow(_dbHelper)
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
            else
            {
                Console.WriteLine("Failed to connect to the database!" );
            }
        }
    }
}