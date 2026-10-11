using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Kronix.Database;
using Notification.Core;
using Notification.Wpf;
using Color = System.Windows.Media.Color;

namespace Kronix.Windows;

public partial class Zeiterfassung : UserControl
{
    private readonly DatabaseHelper _dbHelper;

    private DateTime _startTime;           // To store the start time
    private DispatcherTimer _timer;        // Timer to update elapsed time
    private readonly DispatcherTimer _notesSaveTimer;
    private readonly NotificationManager _notificationManager;
    private int? _activeTimeLogId;
    private bool _updatingNotes;

    public Zeiterfassung(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        this._dbHelper = dbHelper;

        _timer = new DispatcherTimer();
        _notesSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _notesSaveTimer.Tick += NotesSaveTimer_Tick;
        _notificationManager = new NotificationManager();
        Unloaded += Zeiterfassung_Unloaded;

        RestoreActiveTimeLog();
    }

    private void RestoreActiveTimeLog()
    {
        var activeLog = _dbHelper.GetOpenTimeLog();
        if (activeLog?.StartTime is not DateTime startTime)
        {
            return;
        }

        _activeTimeLogId = activeLog.Id;
        _startTime = startTime;
        _updatingNotes = true;
        NotesTextBox.Text = activeLog.Notes ?? string.Empty;
        _updatingNotes = false;
        CustomerNumberTextBox.Text = activeLog.ClientNumber ?? string.Empty;
        CustomerNumberTextBox.IsEnabled = false;
        CustomerNumberTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 129, 72));
        CustomerNumberTextBox.FontWeight = FontWeights.Bold;
        PlaceholderTextBlock.Visibility = Visibility.Collapsed;
        StartTimeTextBlock.Text = startTime.ToString("HH:mm") + " Uhr";
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += UpdateElapsedTime;
        _timer.Start();
        UpdateElapsedTime(null, EventArgs.Empty);
    }

    private void Zeiterfassung_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        SaveActiveNotes();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        string clientNumber = CustomerNumberTextBox.Text.Trim();

        if (!int.TryParse(clientNumber, out _))
        {
            ShowError("Kundennummer fehlt", "Bitte geben Sie eine gültige Kundennummer ein.");
            return;
        }

        try
        {
            if (_dbHelper.GetCustomer(clientNumber) is null)
            {
                var customerEditor = new CustomerEditorWindow(requestedClientNumber: clientNumber)
                {
                    Owner = Window.GetWindow(this)
                };
                if (customerEditor.ShowDialog() != true)
                {
                    return;
                }

                _dbHelper.SaveCustomer(customerEditor.Customer);
            }
        }
        catch (Exception ex)
        {
            ShowError("Kundendaten nicht gespeichert", ex.Message);
            return;
        }
        
        DateTime now = DateTime.Now;
        DateTime startTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        int timeLogId;
        try
        {
            timeLogId = _dbHelper.StartTimeLog(clientNumber, startTime, NotesTextBox.Text);
        }
        catch (Exception ex)
        {
            ShowError("Zeiterfassung konnte nicht starten", ex.Message);
            return;
        }

        _startTime = startTime;
        _activeTimeLogId = timeLogId;
        StartTimeTextBlock.Text = startTime.ToString("HH:mm") + " Uhr";
        
        CustomerNumberTextBox.IsEnabled = false;
        CustomerNumberTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 129, 72));



        CustomerNumberTextBox.FontWeight = FontWeights.Bold;
        
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        
        // Initialize and start the timer for elapsed time
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
            
        _timer.Tick += UpdateElapsedTime;
        _timer.Start();
        
        // Set the start time in StartTimeTextBlock
        // Confirm the time logging start to the user
        _notificationManager.Show("Kronix", $"Zeiterfassung gestartet für Kundennummer: {clientNumber} um {startTime}", NotificationType.Success);
    }


    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        // Retrieve the customer number
        string clientNumber = CustomerNumberTextBox.Text;

        if (_activeTimeLogId is null)
        {
            ShowError("Keine laufende Zeiterfassung", "Aktuell läuft keine Zeiterfassung, die pausiert werden kann.");
            return;
        }

        DateTime pauseTime = DateTime.Now;
        if (!SaveActiveNotes())
        {
            return;
        }
        try
        {
            if (!_dbHelper.StopTimeLog(_activeTimeLogId.Value, pauseTime))
            {
                ShowError("Pause nicht gespeichert", "Der laufende Zeiteintrag wurde nicht gefunden.");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowError("Pause nicht gespeichert", ex.Message);
            return;
        }
        _activeTimeLogId = null;
        
        // Pause the timer to stop updating elapsed time
        _timer.Stop();

        StartButton.IsEnabled = true;
        PauseButton.IsEnabled = false;

        // Confirm the pause to the user
        _notificationManager.Show("Kronix", $"Zeiterfassung pausiert für Kundennummer: {clientNumber} um {pauseTime}", NotificationType.Information);
    }


    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Retrieve the customer number
        string clientNumber = CustomerNumberTextBox.Text;

        if (string.IsNullOrEmpty(clientNumber))
        {
            ShowError("Kundennummer fehlt", "Bitte geben Sie eine gültige Kundennummer ein.");
            return;
        }

        DateTime stopTime = DateTime.Now;
        if (_activeTimeLogId is int timeLogId)
        {
            if (!SaveActiveNotes())
            {
                return;
            }

            try
            {
                if (!_dbHelper.StopTimeLog(timeLogId, stopTime))
                {
                    ShowError("Eintrag nicht gespeichert", "Der laufende Zeiteintrag wurde nicht gefunden.");
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowError("Eintrag nicht gespeichert", ex.Message);
                return;
            }
            _activeTimeLogId = null;
        }
        
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
        NotesTextBox.Clear();
        _timer = new DispatcherTimer();

        // Confirm the stop and save to the user
        _notificationManager.Show("Kronix", $"Zeiterfassung gestoppt und gespeichert für Kundennummer: {clientNumber} um {stopTime}", NotificationType.Success);
    }

    private void ShowError(string title, string message)
    {
        var dialog = new AppMessageWindow(title, message)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private void NotesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingNotes || _activeTimeLogId is null)
        {
            return;
        }

        _notesSaveTimer.Stop();
        _notesSaveTimer.Start();
    }

    private void NotesSaveTimer_Tick(object? sender, EventArgs e)
    {
        _notesSaveTimer.Stop();
        SaveActiveNotes();
    }

    private bool SaveActiveNotes()
    {
        _notesSaveTimer.Stop();
        if (_activeTimeLogId is not int timeLogId)
        {
            return true;
        }

        try
        {
            _dbHelper.UpdateOpenTimeLogNotes(timeLogId, NotesTextBox.Text);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Notiz nicht gespeichert", ex.Message);
            return false;
        }
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
        ElapsedTimeTextBlock.Text =
            $"{(int)elapsedTime.TotalHours:00}:{elapsedTime.Minutes:00}:{elapsedTime.Seconds:00}";
    }

}