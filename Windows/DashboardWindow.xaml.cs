using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Kronix.Database;
using Notification.Wpf;

namespace Kronix.Windows;

public partial class DashboardWindow : Window
{
    private readonly DatabaseHelper _dbHelper;
    private readonly NotifyIcon? _notifyIcon;
    private readonly NotificationManager _notificationManager;

    public DashboardWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        this._dbHelper = dbHelper;
        
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

        // Add a context menu to the NotifyIcon
        if (_notifyIcon == null) return;
        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("Beenden", null, (s, e) => ExitApplication());

        // Handle double-click to open window
        _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        
        _notificationManager = new NotificationManager();
    }
    
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Cancel the close action and minimize to system tray
        e.Cancel = true;
        Hide();
        if (_notifyIcon != null) _notifyIcon.Visible = true;
        ShowInTaskbar = false;
        ShowMessage();
    }
    
    private void ShowMessage()
    {
        // Display the notification
        _notificationManager.Show("Kronix", "Kronix läuft jetzt im Hintergrund!", NotificationType.Information);
    }
    
    // Method to show the main window from the tray
    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        if (_notifyIcon != null) _notifyIcon.Visible = false;
    }

    // Method to exit the application from the tray
    private void ExitApplication()
    {
        _notifyIcon?.Dispose(); // Dispose the icon
        System.Windows.Application.Current.Shutdown(); ; // Shut down the app
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _notifyIcon?.Dispose(); // Dispose of the icon when the window is closed
        base.OnClosed(e);
    }
    
    // Minimize to tray on minimize
    protected override async void OnStateChanged(EventArgs e)
    {
        try
        {
            base.OnStateChanged(e);
            switch (WindowState)
            {
                case WindowState.Minimized:
                    Hide();
                    if (_notifyIcon != null) _notifyIcon.Visible = true;
                    ShowInTaskbar = false;
            
                    await Task.Delay(100); // Delay for smooth transition
                    break;
                case WindowState.Normal:
                case WindowState.Maximized:
                    if (_notifyIcon != null) _notifyIcon.Visible = false; // Hide NotifyIcon when the window is restored
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error in OnStateChanged: " + ex.Message);
        }
    }

    private void ZeiterfassungButton_Click(object sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new Zeiterfassung(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = false;
        KundenBtn.IsEnabled = true;
        DatenBtn.IsEnabled = true;
    }

    private void KundenButton_Click(object sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new KundenWindow(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = false;
        DatenBtn.IsEnabled = true;
    }

    private void DatenButton_Click(object sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new DatenWindow(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = true;
        DatenBtn.IsEnabled = false;
    }
}