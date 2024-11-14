using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using Kronix.Database;
using Notification.Wpf;
// Import for NotifyIcon
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;     

namespace Kronix.Windows;

public partial class Zeiterfassung
{
    private readonly NotifyIcon? _notifyIcon;
    private readonly DatabaseHelper _dbHelper;

    private DateTime _startTime;           // To store the start time
    private DispatcherTimer _timer;        // Timer to update elapsed time
    private readonly NotificationManager _notificationManager;
    private bool _isPaused = false;

    public Zeiterfassung(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        this._dbHelper = dbHelper;

        _timer = new DispatcherTimer();
        
        var iconUri = new Uri("pack://application:,,,/Resources/logo3.ico", UriKind.Absolute);
        Stream? iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;

        if (iconStream != null)
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = new Icon(iconStream), // Load icon from resources
                Visible = true,
                Text = "Kronix"
            };

            // Make sure to dispose the stream if necessary
            iconStream.Dispose();
        }

        _notificationManager = new NotificationManager();

        // Add a context menu to the NotifyIcon
        if (_notifyIcon == null) return;
        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("Pause", null,
            (s, e) => PauseButton_Click(this, new RoutedEventArgs()));
        _notifyIcon.ContextMenuStrip.Items.Add("Stop", null,
            (s, e) => StopButton_Click(this, new RoutedEventArgs()));
        _notifyIcon.ContextMenuStrip.Items.Add("Beenden", null, (s, e) => ExitApplication());
    }
    
    // Method to exit the application from the tray
    private void ExitApplication()
    {
        _notifyIcon?.Dispose(); // Dispose the icon
        System.Windows.Application.Current.Shutdown(); ; // Shut down the app
    }
    
    
    
    // Platzhalter für die Start-Button-Logik
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // Retrieve the customer number from the input text box
        string clientNumber = CustomerNumberTextBox.Text;

        if (string.IsNullOrEmpty(clientNumber))
        {
            MessageBox.Show("Bitte geben Sie eine gültige Kundennummer ein!", "Kronix");
            return;
        }
        
        // Use the _dbHelper instance to start a new time log entry
        DateTime startTime = DateTime.Now; // Record the current time
        _dbHelper.StartTimeLog(clientNumber, startTime);
        
        CustomerNumberTextBox.IsEnabled = false;
        CustomerNumberTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 129, 72));



        CustomerNumberTextBox.FontWeight = FontWeights.Bold;
        
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        
        _startTime = DateTime.Now; // Record the start time
        
        // Initialize and start the timer for elapsed time
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
            
        _timer.Tick += UpdateElapsedTime;
        
        _isPaused = false;
        _timer.Start();
        
        // Set the start time in StartTimeTextBlock
        StartTimeTextBlock.Text = startTime.ToString("HH:mm") + " Uhr";
        
        // Confirm the time logging start to the user
        _notificationManager.Show("Kronix", $"Zeiterfassung gestartet für Kundennummer: {clientNumber} um {startTime}", NotificationType.Success);
    }


    // Platzhalter für die Pause-Button-Logik
    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        // Retrieve the customer number
        string clientNumber = CustomerNumberTextBox.Text;

        if (string.IsNullOrEmpty(clientNumber))
        {
            MessageBox.Show("Bitte geben Sie eine gültige Kundennummer ein.");
            return;
        }

        // Use _dbHelper to stop (pause) the current log entry for this customer
        DateTime pauseTime = DateTime.Now;
        _dbHelper.StopTimeLog(clientNumber, pauseTime);
        
        // Pause the timer to stop updating elapsed time
        _timer.Stop();

        StartButton.IsEnabled = true;
        PauseButton.IsEnabled = false;

        _isPaused = true;

        // Confirm the pause to the user
        _notificationManager.Show("Kronix", $"Zeiterfassung pausiert für Kundennummer: {clientNumber} um {pauseTime}", NotificationType.Information);
    }


    // Platzhalter für die Stop-Button-Logik
    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Retrieve the customer number
        string clientNumber = CustomerNumberTextBox.Text;

        if (string.IsNullOrEmpty(clientNumber))
        {
            MessageBox.Show("Bitte geben Sie eine gültige Kundennummer ein.");
            return;
        }

        // Use _dbHelper to stop (finalize) the current log entry for this customer
        DateTime stopTime = DateTime.Now;
        _dbHelper.StopTimeLog(clientNumber, stopTime);
        
        // Stop the timer to stop updating elapsed time
        _timer.Stop();

        CustomerNumberTextBox.IsEnabled = true;
        CustomerNumberTextBox.Text = "";
        CustomerNumberTextBox.Foreground = new SolidColorBrush(Colors.Black);
        CustomerNumberTextBox.FontWeight = FontWeights.Normal;
        
        StartButton.IsEnabled = true;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        
        StartTimeTextBlock.Text = "--:--";
        ElapsedTimeTextBlock.Text = "--:--:--";
        _timer = new DispatcherTimer();

        // Confirm the stop and save to the user
        _notificationManager.Show("Kronix", $"Zeiterfassung gestoppt und gespeichert für Kundennummer: {clientNumber} um {stopTime}", NotificationType.Success);
    }


    // Zahlenprüfung für die Kundennummer-Eingabe
    private void CustomerNumberTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Überprüfen, ob die Eingabe eine Zahl ist
        e.Handled = !int.TryParse(e.Text, out _);
    }
    
    private void CustomerNumberTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CustomerNumberTextBox.Text))
        {
            PlaceholderTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void CustomerNumberTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CustomerNumberTextBox.Text))
        {
            PlaceholderTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void UpdateElapsedTime(object? sender, EventArgs e)
    {
        // Calculate the elapsed time
        TimeSpan elapsedTime = DateTime.Now - _startTime;

        // Update ElapsedTimeTextBlock with the formatted elapsed time
        ElapsedTimeTextBlock.Text = elapsedTime.ToString(@"hh\:mm\:ss");
    }

    private void ZeiterfassungButton_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void KundenButton_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void DatenButton_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
}