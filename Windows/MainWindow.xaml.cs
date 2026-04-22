using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Kronix.Database;
using Kronix.Logging;
using Kronix.Windows;

namespace Kronix
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    
    public partial class MainWindow : Window
    {
        private DatabaseHelper? _dbHelper;
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        
        public MainWindow()
        {
            InitializeComponent();

            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();
            Loaded += MainWindow_Loaded;
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            ClockTextBlock.Text = now.ToString("HH:mm:ss");
            DateTextBlock.Text = now.ToString("dddd, d. MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"));
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeDatabaseAsync();
        }

        private async Task InitializeDatabaseAsync()
        {
            StartButton.IsEnabled = false;
            InitializationProgressBar.Visibility = Visibility.Visible;
            StatusIndicator.Visibility = Visibility.Collapsed;
            StatusText.Text = "Kronix wird gestartet …";

            try
            {
                IProgress<string> progress = new Progress<string>(status =>
                {
                    StatusText.Text = status;
                });
                DatabaseHelper dbHelper = await Task.Run(() => new DatabaseHelper(progress.Report));
                _dbHelper = dbHelper;
                App.DbHelper = dbHelper;
                if (!dbHelper.IsDatabaseInitialized())
                {
                    AppLogger.Warning("database.startup_unavailable",
                        "The application startup database check reported that the database is unavailable.");
                    throw new InvalidOperationException(
                        $"Datenbankverbindung fehlgeschlagen: {dbHelper.InitializationError ?? "Unbekannter Fehler."}");
                }

                AppLogger.Information("database.startup_ready", "The startup database check succeeded.");
                StatusText.Text = "Datenbank bereit";
                StatusIndicator.Fill = System.Windows.Media.Brushes.SeaGreen;
                StatusIndicator.Visibility = Visibility.Visible;
                StartButton.Content = "Starten";
                StartButton.IsEnabled = true;
                RetryButton.Visibility = Visibility.Collapsed;
                InitializationProgressBar.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                AppLogger.Error("application.startup_database_failed",
                    "The startup database initialization flow failed.", ex);
                StatusText.Text = ex.Message;
                StatusIndicator.Fill = System.Windows.Media.Brushes.IndianRed;
                StatusIndicator.Visibility = Visibility.Visible;
                StartButton.Content = _dbHelper is null ? "Erneut versuchen" : "Trotzdem öffnen";
                StartButton.IsEnabled = true;
                RetryButton.Visibility = Visibility.Visible;
                InitializationProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dbHelper?.IsDatabaseInitialized() != true)
            {
                if (_dbHelper is null)
                {
                    await InitializeDatabaseAsync();
                    if (_dbHelper is null)
                    {
                        return;
                    }
                }
            }

            var dashboardWindow = new DashboardWindow(_dbHelper);
            dashboardWindow.Show();
            AppLogger.Information("application.dashboard_opened", "The main dashboard was opened.");
            Close();
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            await InitializeDatabaseAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer.Stop();
            base.OnClosed(e);
        }
    }
}