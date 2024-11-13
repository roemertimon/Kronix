using System;
using System.Windows;
using System.Windows.Forms; // Import for NotifyIcon
using System.Windows.Input;
using System.Drawing;
using System.Threading.Tasks;
using Notification.Wpf; // Import for Icon
using MessageBox = System.Windows.MessageBox;     

namespace Kronix;

public partial class DashboardWindow : Window
{
    private NotifyIcon notifyIcon;
    
    public DashboardWindow()
    {
        InitializeComponent();

        // Initialize NotifyIcon
        notifyIcon = new NotifyIcon
        {
            Icon = new Icon(SystemIcons.Asterisk, 40, 40), // Set your custom icon here
            Visible = false,
            Text = "Kronix"
        };

        // Add a context menu to the NotifyIcon
        notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        notifyIcon.ContextMenuStrip.Items.Add("Öffnen", null, (s, e) => ShowWindow());
        notifyIcon.ContextMenuStrip.Items.Add("Beenden", null, (s, e) => ExitApplication());

        // Handle double-click to open window
        notifyIcon.Click += (s, e) => ShowWindow();
    }

    private static void ShowMessage()
    {
        var notificationManager = new NotificationManager();

        // Display the notification
        notificationManager.Show("Kronix", "Kronix läuft jetzt im Hintergrund!", NotificationType.Information);
    }

    // Override the window closing event
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Cancel the close action and minimize to system tray
        e.Cancel = true;
        Hide();
        notifyIcon.Visible = true;
        ShowInTaskbar = false;
        ShowMessage();
    }

    // Method to show the main window from the tray
    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        notifyIcon.Visible = false;
    }

    // Method to exit the application from the tray
    private void ExitApplication()
    {
        notifyIcon.Dispose(); // Dispose the icon
        System.Windows.Application.Current.Shutdown(); ; // Shut down the app
    }
    
    protected override void OnClosed(EventArgs e)
    {
        notifyIcon.Dispose(); // Dispose of the icon when the window is closed
        base.OnClosed(e);
    }

    // Minimize to tray on minimize
    protected override async void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        switch (WindowState)
        {
            case WindowState.Minimized:
                Hide();
                notifyIcon.Visible = true;
                ShowInTaskbar = false;
            
                await Task.Delay(100); // Delay for smooth transition
                break;
            case WindowState.Normal:
            case WindowState.Maximized:
                notifyIcon.Visible = false; // Hide NotifyIcon when the window is restored
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    
    // Platzhalter für die Start-Button-Logik
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // Start der Zeiterfassung für den Kunden
        MessageBox.Show("Zeiterfassung gestartet für Kundennummer: " + CustomerNumberTextBox.Text);
    }

    // Platzhalter für die Pause-Button-Logik
    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        // Zeiterfassung pausieren
        MessageBox.Show("Zeiterfassung pausiert für Kundennummer: " + CustomerNumberTextBox.Text);
    }

    // Platzhalter für die Stop-Button-Logik
    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Zeiterfassung stoppen und Daten speichern
        MessageBox.Show("Zeiterfassung gestoppt und gespeichert für Kundennummer: " + CustomerNumberTextBox.Text);
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

}