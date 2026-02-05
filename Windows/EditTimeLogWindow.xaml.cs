using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kronix.Database;

namespace Kronix.Windows;

public partial class EditTimeLogWindow : Window
{
    public string ClientNumber { get; private set; } = string.Empty;
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }
    public string Notes => NotesTextBox.Text.Trim();

    public EditTimeLogWindow(DatabaseHelper.TimeLog timeLog)
    {
        InitializeComponent();
        ClientNumberTextBox.Text = timeLog.ClientNumber;
        StartTimeTextBox.Text = timeLog.StartTime?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
        EndTimeTextBox.Text = timeLog.EndTime?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
        NotesTextBox.Text = timeLog.Notes ?? string.Empty;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string clientNumber = ClientNumberTextBox.Text.Trim();
        if (clientNumber.Length == 0)
        {
            ShowValidationError("Bitte geben Sie eine Kundennummer ein.");
            return;
        }

        if (!DateTime.TryParseExact(StartTimeTextBox.Text.Trim(), "dd.MM.yyyy HH:mm",
                CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out DateTime startTime))
        {
            ShowValidationError("Bitte geben Sie eine gültige Startzeit im Format TT.MM.JJJJ HH:MM ein.");
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
