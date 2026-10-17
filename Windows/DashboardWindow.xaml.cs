using System;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;
using Kronix.Database;
using Kronix.Logging;

namespace Kronix.Windows;

public partial class DashboardWindow : Window
{
    private static DashboardWindow? _trayOwner;
    private static WinForms.NotifyIcon? _notifyIcon;
    private static WinForms.ToolStripMenuItem? _stopTrackingMenuItem;

    private readonly DatabaseHelper _dbHelper;
    private bool _allowClose;

    public DashboardWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        this._dbHelper = dbHelper;
        _trayOwner = this;
        EnsureTrayIcon();

        _dbHelper.DatabaseStateChanged += DatabaseStateChanged;
        ShowZeiterfassung();
        UpdateDatabaseWarning();
    }
    
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        AppLogger.Information("window.hidden_to_tray", "The dashboard was hidden to the system tray.");
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
        new KronixToastWindow("Kronix läuft im Hintergrund",
            "Die Anwendung ist weiterhin über das Symbol im Infobereich erreichbar.",
            isSuccess: false).Show();
    }

    private void DatabaseStateChanged(object? sender, EventArgs e)
    {
        UpdateDatabaseWarning();
    }

    private void UpdateDatabaseWarning()
    {
        bool isReady = _dbHelper.IsDatabaseInitialized();
        DatabaseWarningBorder.Visibility = isReady ? Visibility.Collapsed : Visibility.Visible;
        DatabaseWarningTextBlock.Text = _dbHelper.InitializationError ??
                                        "Datenbank nicht verfügbar. Öffnen Sie Einstellungen, um eine Datenbank auszuwählen oder neu anzulegen.";
    }

    private void UpdateTrayMenu()
    {
        if (_stopTrackingMenuItem is null)
        {
            return;
        }

        if (!_dbHelper.IsDatabaseInitialized())
        {
            _stopTrackingMenuItem.Enabled = false;
            _stopTrackingMenuItem.Text = "Datenbank nicht verfügbar";
            return;
        }

        try
        {
            DatabaseHelper.TimeLog? openLog = _dbHelper.GetOpenTimeLog();
            _stopTrackingMenuItem.Enabled = openLog is not null;
            _stopTrackingMenuItem.Text = openLog is null
                ? "Keine laufende Zeiterfassung"
                : $"Zeiterfassung stoppen (Kunde {openLog.ClientNumber})";
        }
        catch (Exception ex)
        {
            _stopTrackingMenuItem.Enabled = false;
            _stopTrackingMenuItem.Text = "Zeiterfassung nicht verfügbar";
            AppLogger.Error("tray.active_tracking_read_failed",
                "Could not read the active time-tracking state for the tray menu.", ex);
        }
    }

    private void StopTrackingFromTray()
    {
        try
        {
            DatabaseHelper.TimeLog? openLog = _dbHelper.GetOpenTimeLog();
            if (openLog is null)
            {
                return;
            }

            if (!_dbHelper.StopTimeLog(openLog.Id, DateTime.Now))
            {
                throw new InvalidOperationException("Der laufende Zeiteintrag wurde nicht gefunden.");
            }

            _notifyIcon?.ShowBalloonTip(
                3000,
                "Kronix",
                $"Zeiterfassung für Kunde {openLog.ClientNumber} wurde gestoppt.",
                WinForms.ToolTipIcon.Info);
            AppLogger.Information("timelog.stopped_from_tray",
                "An active time-tracking entry was stopped from the system tray.");
            ShowZeiterfassung();
            UpdateTrayMenu();
        }
        catch (Exception ex)
        {
            AppLogger.Error("timelog.tray_stop_failed",
                "Could not stop the active time-tracking entry from the system tray.", ex);
            _notifyIcon?.ShowBalloonTip(4000, "Kronix", $"Stoppen fehlgeschlagen: {ex.Message}",
                WinForms.ToolTipIcon.Error);
        }
    }
    
    // Method to show the main window from the tray
    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
        }
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
        _dbHelper.DatabaseStateChanged -= DatabaseStateChanged;
        if (_allowClose && ReferenceEquals(_trayOwner, this))
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            _stopTrackingMenuItem = null;
            _trayOwner = null;
            AppLogger.Information("application.exit_requested", "The application was closed from the system tray.");
        }

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
            _notifyIcon.Visible = true;
        }
    }

    private void ZeiterfassungButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Information("navigation.time_tracking", "Opened the time-tracking tab.");
        ShowZeiterfassung();
    }

    private void ShowZeiterfassung()
    {
        MainContentControl.Content = new Zeiterfassung(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = false;
        KundenBtn.IsEnabled = true;
        MitarbeiterBtn.IsEnabled = true;
        DatenBtn.IsEnabled = true;
        EinstellungenBtn.IsEnabled = true;
    }

    private void KundenButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Information("navigation.customers", "Opened the customers tab.");
        MainContentControl.Content = new KundenWindow(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = false;
        MitarbeiterBtn.IsEnabled = true;
        DatenBtn.IsEnabled = true;
        EinstellungenBtn.IsEnabled = true;
    }

    private void MitarbeiterButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Information("navigation.employees", "Opened the employees tab.");
        MainContentControl.Content = new MitarbeiterWindow(_dbHelper);

        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = true;
        MitarbeiterBtn.IsEnabled = false;
        DatenBtn.IsEnabled = true;
        EinstellungenBtn.IsEnabled = true;
    }

    private void DatenButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Information("navigation.time_logs", "Opened the time entries tab.");
        MainContentControl.Content = new DatenWindow(_dbHelper);
        
        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = true;
        MitarbeiterBtn.IsEnabled = true;
        DatenBtn.IsEnabled = false;
        EinstellungenBtn.IsEnabled = true;
    }

    private void EinstellungenButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Information("navigation.settings", "Opened the settings tab.");
        MainContentControl.Content = new EinstellungenWindow(_dbHelper);

        ZeiterfassungBtn.IsEnabled = true;
        KundenBtn.IsEnabled = true;
        MitarbeiterBtn.IsEnabled = true;
        DatenBtn.IsEnabled = true;
        EinstellungenBtn.IsEnabled = false;
    }

    private static void EnsureTrayIcon()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
            return;
        }

        var iconUri = new Uri("pack://application:,,,/Resources/logo3.ico", UriKind.Absolute);
        using Stream? iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
        if (iconStream is null)
        {
            throw new InvalidOperationException("Das Kronix-Symbol für den Infobereich konnte nicht geladen werden.");
        }

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream),
            Visible = true,
            Text = "Kronix"
        };

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("Kronix öffnen", null, (_, _) => _trayOwner?.ShowWindow());
        _stopTrackingMenuItem = new WinForms.ToolStripMenuItem("Zeiterfassung stoppen");
        _stopTrackingMenuItem.Click += (_, _) => _trayOwner?.StopTrackingFromTray();
        contextMenu.Items.Add(_stopTrackingMenuItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("Beenden", null, (_, _) => _trayOwner?.ExitApplication());
        contextMenu.Opening += (_, _) => _trayOwner?.UpdateTrayMenu();
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => _trayOwner?.ShowWindow();
    }
}