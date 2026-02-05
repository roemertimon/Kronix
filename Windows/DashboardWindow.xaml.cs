using System;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;
using Kronix.Database;
using Notification.Core;
using Notification.Wpf;

namespace Kronix.Windows;

public partial class DashboardWindow : Window
{
    private readonly DatabaseHelper _dbHelper;
    private readonly WinForms.NotifyIcon? _notifyIcon;
    private readonly NotificationManager _notificationManager = new();
    private bool _allowClose;

    public DashboardWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        this._dbHelper = dbHelper;
        
        var iconUri = new Uri("pack://application:,,,/Resources/logo3.ico", UriKind.Absolute);
        using Stream? iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;

        if (iconStream != null)
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(iconStream),
                Visible = true,
                Text = "Kronix"
            };
            _notifyIcon.ContextMenuStrip = new WinForms.ContextMenuStrip();
            _notifyIcon.ContextMenuStrip.Items.Add("Beenden", null, (s, e) => ExitApplication());
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        }

        ShowZeiterfassung();
    }
    
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
        }
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
        _allowClose = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnClosed(e);
    }
    
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
        else if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }
    }

    private void ZeiterfassungButton_Click(object sender, RoutedEventArgs e)
    {
        ShowZeiterfassung();
    }

    private void ShowZeiterfassung()
    {
        MainContentControl.Content = new Zeiterfassung(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = false;
        KundenBtn.IsEnabled = true;
        DatenBtn.IsEnabled = true;
        EinstellungenBtn.IsEnabled = true;
    }

    private void KundenButton_Click(object sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new KundenWindow(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = false;
        DatenBtn.IsEnabled = true;
        EinstellungenBtn.IsEnabled = true;
    }

    private void DatenButton_Click(object sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new DatenWindow(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = true;
        DatenBtn.IsEnabled = false;
        EinstellungenBtn.IsEnabled = true;
    }

    private void EinstellungenButton_Click(object sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new EinstellungenWindow(_dbHelper);

        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = true;
        DatenBtn.IsEnabled = true;
        EinstellungenBtn.IsEnabled = false;
    }
}