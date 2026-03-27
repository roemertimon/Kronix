using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
    private readonly ObservableCollection<DatabaseHelper.Customer> _customers = new();
    private readonly ICollectionView _customersView;

    private DateTime _startTime;           // To store the start time
    private DispatcherTimer _timer;        // Timer to update elapsed time
    private readonly DispatcherTimer _notesSaveTimer;
    private readonly NotificationManager _notificationManager;
    private int? _activeTimeLogId;
    private bool _updatingNotes;
    private bool _updatingCustomerSelection;

    public Zeiterfassung(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        this._dbHelper = dbHelper;
        _customersView = CollectionViewSource.GetDefaultView(_customers);
        _customersView.Filter = FilterCustomerSuggestion;
        CustomerNumberComboBox.ItemsSource = _customersView;

        _timer = new DispatcherTimer();
        _notesSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _notesSaveTimer.Tick += NotesSaveTimer_Tick;
        _notificationManager = new NotificationManager();
        Unloaded += Zeiterfassung_Unloaded;

        RestoreActiveTimeLog();
    }

    private void RestoreActiveTimeLog()
    {
        if (!_dbHelper.IsDatabaseInitialized())
        {
            return;
        }

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
        CustomerNumberComboBox.Text = activeLog.ClientNumber ?? string.Empty;
        CustomerNumberComboBox.IsEnabled = false;
        CustomerNumberComboBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 129, 72));
        CustomerNumberComboBox.FontWeight = FontWeights.Bold;
        UpdateCustomerName(activeLog.ClientNumber);
        StartTimeTextBlock.Text = startTime.ToString("HH:mm") + " Uhr";
        EditStartTimeButton.Visibility = Visibility.Visible;
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
        if (!_dbHelper.IsDatabaseInitialized())
        {
            ShowError("Datenbank nicht verfügbar",
                _dbHelper.InitializationError ??
                "Öffnen Sie Einstellungen, um eine Datenbank auszuwählen oder neu anzulegen.");
            return;
        }

        string clientNumber = CustomerNumberComboBox.Text.Trim();

        if (!int.TryParse(clientNumber, out _))
        {
            ShowError("Kundennummer fehlt", "Bitte geben Sie eine gültige Kundennummer ein.");
            return;
        }

        try
        {
            DatabaseHelper.Customer? customer = _dbHelper.GetCustomer(clientNumber);
            if (customer is null)
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
                customer = customerEditor.Customer;
            }

            UpdateCustomerName(customer);
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
        EditStartTimeButton.Visibility = Visibility.Visible;
        
        CustomerNumberComboBox.IsEnabled = false;
        CustomerNumberComboBox.Foreground = new SolidColorBrush(Color.FromRgb(0, 129, 72));

        CustomerNumberComboBox.FontWeight = FontWeights.Bold;
        
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
        string clientNumber = CustomerNumberComboBox.Text;

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
        string clientNumber = CustomerNumberComboBox.Text;

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

        CustomerNumberComboBox.IsEnabled = true;
        CustomerNumberComboBox.SelectedItem = null;
        CustomerNumberComboBox.Text = "";
        CustomerNumberComboBox.Foreground = new SolidColorBrush(Colors.Black);
        CustomerNumberComboBox.FontWeight = FontWeights.Normal;
        CustomerNameTextBlock.Visibility = Visibility.Hidden;
        
        StartButton.IsEnabled = true;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        
        StartTimeTextBlock.Text = "--:--";
        EditStartTimeButton.Visibility = Visibility.Collapsed;
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


    private void CustomerNumberComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        _customers.Clear();
        if (_dbHelper.IsDatabaseInitialized())
        {
            foreach (DatabaseHelper.Customer customer in _dbHelper.GetAllCustomers())
            {
                _customers.Add(customer);
            }
        }

        UpdateCustomerName(CustomerNumberComboBox.Text);
        if (CustomerNumberComboBox.Template.FindName("PART_EditableTextBox", CustomerNumberComboBox) is TextBox textBox)
        {
            textBox.TextChanged += CustomerNumberTextBox_TextChanged;
            textBox.PreviewTextInput += CustomerNumberTextBox_PreviewTextInput;
            textBox.GotFocus += CustomerNumberTextBox_GotFocus;
            textBox.LostFocus += CustomerNumberTextBox_LostFocus;
        }
    }

    private void CustomerNumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void CustomerNumberComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomerNumberComboBox.SelectedItem is DatabaseHelper.Customer customer)
        {
            _updatingCustomerSelection = true;
            CustomerNumberComboBox.Text = customer.ClientNumber;
            _updatingCustomerSelection = false;
            UpdateCustomerName(customer);
            CustomerNumberComboBox.IsDropDownOpen = false;
        }
    }

    private void CustomerNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingCustomerSelection)
        {
            return;
        }

        string text = CustomerNumberComboBox.Text;
        if (CustomerNumberComboBox.SelectedItem is DatabaseHelper.Customer selectedCustomer &&
            !string.Equals(selectedCustomer.ClientNumber, text, StringComparison.Ordinal))
        {
            CustomerNumberComboBox.SelectedItem = null;
        }

        _customersView.Refresh();
        UpdateCustomerName(text);
        CustomerNumberComboBox.IsDropDownOpen =
            text.Length > 0 && _customersView.Cast<DatabaseHelper.Customer>().Any();
    }

    private bool FilterCustomerSuggestion(object item)
    {
        if (item is not DatabaseHelper.Customer customer)
        {
            return false;
        }

        string filter = CustomerNumberComboBox.Text.Trim();
        return filter.Length == 0 ||
               customer.ClientNumber.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
               customer.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
               customer.Company.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateCustomerName(string? clientNumber)
    {
        DatabaseHelper.Customer? customer = _customers.FirstOrDefault(item =>
            string.Equals(item.ClientNumber, clientNumber?.Trim(), StringComparison.OrdinalIgnoreCase));
        UpdateCustomerName(customer);
    }

    private void UpdateCustomerName(DatabaseHelper.Customer? customer)
    {
        if (customer is null)
        {
            CustomerNameTextBlock.Text = string.IsNullOrWhiteSpace(CustomerNumberComboBox.Text)
                ? string.Empty
                : "Kunde noch nicht vorhanden – beim Start können Sie ihn anlegen.";
            CustomerNameTextBlock.Foreground = customer is null && !string.IsNullOrWhiteSpace(CustomerNumberComboBox.Text)
                ? new SolidColorBrush(Color.FromRgb(152, 101, 0))
                : new SolidColorBrush(Color.FromRgb(102, 112, 133));
            CustomerNameTextBlock.Visibility = string.IsNullOrWhiteSpace(CustomerNumberComboBox.Text)
                ? Visibility.Hidden
                : Visibility.Visible;
            return;
        }

        string name = string.IsNullOrWhiteSpace(customer.DisplayName)
            ? "Kundenstammdaten unvollständig"
            : customer.DisplayName;
        if (!string.IsNullOrWhiteSpace(customer.Company))
        {
            name += $" · {customer.Company}";
        }

        CustomerNameTextBlock.Text = name;
        CustomerNameTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133));
        CustomerNameTextBlock.Visibility = Visibility.Visible;
    }

    private void CustomerNumberTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_customers.Count > 0)
        {
            CustomerNumberComboBox.IsDropDownOpen = true;
        }
    }

    private void CustomerNumberTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CustomerNumberComboBox.IsDropDownOpen = false;
    }

    private void UpdateElapsedTime(object? sender, EventArgs e)
    {
        // Calculate the elapsed time
        TimeSpan elapsedTime = DateTime.Now - _startTime;

        // Update ElapsedTimeTextBlock with the formatted elapsed time
        ElapsedTimeTextBlock.Text =
            $"{(int)elapsedTime.TotalHours:00}:{elapsedTime.Minutes:00}:{elapsedTime.Seconds:00}";
    }

    private void EditStartTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTimeLogId is not int timeLogId)
        {
            return;
        }

        var activeLog = new DatabaseHelper.TimeLog
        {
            Id = timeLogId,
            ClientNumber = CustomerNumberComboBox.Text,
            StartTime = _startTime,
            EndTime = null,
            Notes = NotesTextBox.Text
        };
        var editor = new EditTimeLogWindow(activeLog)
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _dbHelper.UpdateOpenTimeLogStartTime(timeLogId, editor.StartTime);
            _startTime = editor.StartTime;
            StartTimeTextBlock.Text = _startTime.ToString("HH:mm") + " Uhr";
            UpdateElapsedTime(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowError("Startzeit nicht geändert", ex.Message);
        }
    }

}