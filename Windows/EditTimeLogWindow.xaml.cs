using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kronix.Database;

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
            StartTimeTextBox.Text = roundedNow.AddMinutes(-5).ToString("dd.MM.yyyy HH:mm");
            EndTimeTextBox.Text = roundedNow.ToString("dd.MM.yyyy HH:mm");
            return;
        }

        ClientNumberTextBox.Text = timeLog.ClientNumber;
        StartTimeTextBox.Text = timeLog.StartTime?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
        EndTimeTextBox.Text = timeLog.EndTime?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
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
        if (IsNewEntry && !int.TryParse(clientNumber, out _))
        {
            ShowValidationError("Die Kundennummer muss eine Zahl sein.");
            return;
        }

        if (!DateTime.TryParseExact(StartTimeTextBox.Text.Trim(), "dd.MM.yyyy HH:mm",
                CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out DateTime startTime))
        {
            ShowValidationError("Bitte geben Sie eine gültige Startzeit im Format TT.MM.JJJJ HH:MM ein.");
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

        if (!DateTime.TryParseExact(EndTimeTextBox.Text.Trim(), "dd.MM.yyyy HH:mm",
                CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out DateTime endTime))
        {
            ShowValidationError("Bitte geben Sie eine gültige Endzeit im Format TT.MM.JJJJ HH:MM ein.");
            return;
        }

        if (endTime < startTime)
        {
            ShowValidationError("Die Endzeit darf nicht vor der Startzeit liegen.");
            return;
        }

        if (IsNewEntry && endTime > DateTime.Now)
        {
            ShowValidationError("Das Ende eines manuellen Eintrags darf nicht in der Zukunft liegen.");
            return;
        }

        if (IsNewEntry && startTime > DateTime.Now)
        {
            ShowValidationError("Der Beginn eines manuellen Eintrags darf nicht in der Zukunft liegen.");
            return;
        }

        ClientNumber = clientNumber;
        StartTime = startTime;
        EndTime = endTime;
        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void RoundEndTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DateTime.TryParseExact(StartTimeTextBox.Text.Trim(), "dd.MM.yyyy HH:mm",
                CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out DateTime startTime))
        {
            ShowValidationError("Bitte geben Sie zuerst eine gültige Startzeit ein.");
            return;
        }

        if (!DateTime.TryParseExact(EndTimeTextBox.Text.Trim(), "dd.MM.yyyy HH:mm",
                CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out DateTime endTime))
        {
            ShowValidationError("Bitte geben Sie zuerst eine gültige Endzeit ein.");
            return;
        }

        if (endTime < startTime)
        {
            ShowValidationError("Die Endzeit darf nicht vor der Startzeit liegen.");
            return;
        }

        double elapsedMinutes = (endTime - startTime).TotalMinutes;
        int roundedIntervals = Math.Max(1, (int)Math.Ceiling(elapsedMinutes / 5));
        EndTimeTextBox.Text = startTime.AddMinutes(roundedIntervals * 5)
            .ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));
        ErrorTextBlock.Visibility = Visibility.Collapsed;
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
