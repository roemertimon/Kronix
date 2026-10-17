using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kronix.Database;
using Kronix.Logging;

namespace Kronix.Windows;

public partial class EditTimeLogWindow : Window
{
    private readonly DatabaseHelper.TimeLog? _originalTimeLog;

    public string ClientNumber { get; private set; } = string.Empty;
    public DateTime StartTime { get; private set; }
    public DateTime? EndTime { get; private set; }
    public string Notes => NotesTextBox.Text.Trim();
    public bool IsNewEntry => _originalTimeLog is null;
    public bool IsOpenEntry => _originalTimeLog?.EndTime is null && _originalTimeLog is not null;

    public EditTimeLogWindow(DatabaseHelper.TimeLog? timeLog = null)
    {
        InitializeComponent();
        _originalTimeLog = timeLog;
        if (timeLog is null)
        {
            TitleTextBlock.Text = "Eintrag manuell hinzufügen";
            SubtitleTextBlock.Text = "Ergänzen Sie Kundennummer, Arbeitszeit und Tätigkeit.";
            DateTime now = DateTime.Now;
            DateTime roundedNow = new(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            SetDateTime(StartDatePicker, StartTimeTextBox, roundedNow.AddMinutes(-5));
            SetDateTime(EndDatePicker, EndTimeTextBox, roundedNow);
            return;
        }

        ClientNumberTextBox.Text = timeLog.ClientNumber;
        if (timeLog.StartTime is DateTime startTime)
        {
            SetDateTime(StartDatePicker, StartTimeTextBox, startTime);
        }

        if (timeLog.EndTime is DateTime endTime)
        {
            SetDateTime(EndDatePicker, EndTimeTextBox, endTime);
        }

        NotesTextBox.Text = timeLog.Notes ?? string.Empty;
        if (timeLog.EndTime is null)
        {
            TitleTextBlock.Text = "Laufenden Timer korrigieren";
            SubtitleTextBlock.Text = "Passen Sie den tatsächlichen Arbeitsbeginn an.";
            ClientNumberTextBox.IsEnabled = false;
            EndTimeSection.Visibility = Visibility.Collapsed;
            NotesSection.Visibility = Visibility.Collapsed;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string clientNumber = ClientNumberTextBox.Text.Trim();
        if (clientNumber.Length == 0)
        {
            ShowValidationError("Bitte geben Sie eine Kundennummer ein.");
            return;
        }
        if (!TryGetDateTime(StartDatePicker, StartTimeTextBox, out DateTime startTime))
        {
            ShowValidationError("Bitte geben Sie ein gültiges Startdatum und eine Uhrzeit im Format HH:mm ein.");
            return;
        }

        if (IsOpenEntry)
        {
            if (startTime > DateTime.Now)
            {
                ShowValidationError("Die Startzeit darf nicht in der Zukunft liegen.");
                return;
            }

            ClientNumber = _originalTimeLog!.ClientNumber ?? string.Empty;
            StartTime = startTime;
            EndTime = null;
            DialogResult = true;
            return;
        }

        if (!TryGetDateTime(EndDatePicker, EndTimeTextBox, out DateTime endTime))
        {
            ShowValidationError("Bitte geben Sie ein gültiges Enddatum und eine Uhrzeit im Format HH:mm ein.");
            return;
        }

        if (endTime < startTime)
        {
            ShowValidationError("Die Endzeit darf nicht vor der Startzeit liegen.");
            return;
        }

        if (endTime > DateTime.Now)
        {
            ShowValidationError("Die Endzeit darf nicht in der Zukunft liegen.");
            return;
        }

        if (startTime > DateTime.Now)
        {
            ShowValidationError("Die Startzeit darf nicht in der Zukunft liegen.");
            return;
        }

        ClientNumber = clientNumber;
        StartTime = startTime;
        EndTime = endTime;
        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
        AppLogger.Warning("validation.time_entry", "A time-entry form submission failed validation.");
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void RoundEndTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateTime(StartDatePicker, StartTimeTextBox, out DateTime startTime))
        {
            ShowValidationError("Bitte geben Sie zuerst ein gültiges Startdatum und eine Uhrzeit im Format HH:mm ein.");
            return;
        }

        if (!TryGetDateTime(EndDatePicker, EndTimeTextBox, out DateTime endTime))
        {
            ShowValidationError("Bitte geben Sie zuerst ein gültiges Enddatum und eine Uhrzeit im Format HH:mm ein.");
            return;
        }

        if (endTime < startTime)
        {
            ShowValidationError("Die Endzeit darf nicht vor der Startzeit liegen.");
            return;
        }

        double elapsedMinutes = (endTime - startTime).TotalMinutes;
        int roundedIntervals = Math.Max(1, (int)Math.Ceiling(elapsedMinutes / 5));
        SetDateTime(EndDatePicker, EndTimeTextBox,
            startTime.AddMinutes(roundedIntervals * 5));
        ErrorTextBlock.Visibility = Visibility.Collapsed;
    }

    private void RoundStartTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateTime(StartDatePicker, StartTimeTextBox, out DateTime startTime))
        {
            ShowValidationError("Bitte geben Sie zuerst ein gültiges Startdatum und eine Uhrzeit im Format HH:mm ein.");
            return;
        }

        int minutesToAdd = (5 - startTime.Minute % 5) % 5;
        SetDateTime(StartDatePicker, StartTimeTextBox, startTime.AddMinutes(minutesToAdd));
        ErrorTextBlock.Visibility = Visibility.Collapsed;
    }

    private static bool TryGetDateTime(DatePicker datePicker, TextBox timeTextBox,
        out DateTime value)
    {
        value = default;
        if (datePicker.SelectedDate is not DateTime date ||
            !DateTime.TryParseExact(timeTextBox.Text.Trim(), "HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return false;
        }

        value = date.Date.Add(time.TimeOfDay);
        return true;
    }

    private static void SetDateTime(DatePicker datePicker, TextBox timeTextBox, DateTime value)
    {
        datePicker.SelectedDate = value.Date;
        timeTextBox.Text = value.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            (Keyboard.FocusedElement != NotesTextBox || (Keyboard.Modifiers & ModifierKeys.Control) != 0))
        {
            SaveButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
