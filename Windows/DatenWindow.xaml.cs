using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Kronix.Database;
using Microsoft.Win32;

namespace Kronix.Windows;

public partial class DatenWindow : UserControl
{
    private readonly DatabaseHelper _dbHelper;
    private readonly ObservableCollection<DatabaseHelper.TimeLog> _timeLogs = new();
    private readonly ICollectionView _timeLogsView;

    public DatenWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        _dbHelper = dbHelper;
        _timeLogsView = CollectionViewSource.GetDefaultView(_timeLogs);
        _timeLogsView.Filter = FilterTimeLog;
        DataGridRecords.ItemsSource = _timeLogsView;
        LoadAllTimeLogs();
    }

    private void LoadAllTimeLogs()
    {
        _timeLogs.Clear();
        foreach (DatabaseHelper.TimeLog log in _dbHelper.GetAllTimeLogs())
        {
            _timeLogs.Add(log);
        }

        _timeLogsView.Refresh();
        UpdateResultsCount();
    }

    private void ApplyFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurrentFilters();
    }

    private bool ApplyCurrentFilters()
    {
        if (FromDatePicker.SelectedDate is DateTime fromDate &&
            ToDatePicker.SelectedDate is DateTime toDate &&
            fromDate.Date > toDate.Date)
        {
            ShowError("Zeitraum ungültig", "Das Startdatum darf nicht nach dem Enddatum liegen.");
            return false;
        }

        _timeLogsView.Refresh();
        UpdateResultsCount();
        return true;
    }

    private void CustomerFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshFiltersIfValid();
    }

    private void DateFilter_Changed(object? sender, SelectionChangedEventArgs e)
    {
        RefreshFiltersIfValid();
    }

    private void RefreshFiltersIfValid()
    {
        if (FromDatePicker.SelectedDate is DateTime fromDate &&
            ToDatePicker.SelectedDate is DateTime toDate &&
            fromDate.Date > toDate.Date)
        {
            return;
        }

        _timeLogsView.Refresh();
        UpdateResultsCount();
    }

    private void FilterTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFiltersButton_Click(sender, new RoutedEventArgs());
        }
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        CustomerFilterTextBox.Clear();
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        _timeLogsView.Refresh();
        UpdateResultsCount();
    }

    private bool FilterTimeLog(object item)
    {
        if (item is not DatabaseHelper.TimeLog log)
        {
            return false;
        }

        string[] customerNumbers = CustomerFilterTextBox.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (customerNumbers.Length > 0 &&
            !customerNumbers.Contains(log.ClientNumber, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        DateTime? fromDate = FromDatePicker.SelectedDate?.Date;
        DateTime? toDate = ToDatePicker.SelectedDate?.Date;
        if (log.StartTime is not DateTime startTime)
        {
            return !fromDate.HasValue && !toDate.HasValue;
        }

        return (!fromDate.HasValue || startTime.Date >= fromDate.Value) &&
               (!toDate.HasValue || startTime.Date <= toDate.Value);
    }

    private void UpdateResultsCount()
    {
        List<DatabaseHelper.TimeLog> filteredLogs = _timeLogsView.Cast<DatabaseHelper.TimeLog>().ToList();
        int count = filteredLogs.Count;
        DateTime now = DateTime.Now;
        TimeSpan totalDuration = TimeSpan.FromTicks(filteredLogs.Sum(log =>
            (log.Duration ?? (log.StartTime is DateTime startTime && log.EndTime is null
                ? now - startTime
                : TimeSpan.Zero)).Ticks));
        string countText = count == 1 ? "1 Eintrag" : $"{count} Einträge";
        ResultsCountTextBlock.Text = countText;
        TotalDurationTextBlock.Text = FormatSummaryDuration(totalDuration);
    }

    private void BilledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not DatabaseHelper.TimeLog log)
        {
            return;
        }

        try
        {
            _dbHelper.UpdateTimeLogBilled(log.Id, checkBox.IsChecked == true);
            log.IsBilled = checkBox.IsChecked == true;
        }
        catch (Exception ex)
        {
            ShowError("Abrechnung nicht gespeichert", ex.Message);
            LoadAllTimeLogs();
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.TimeLog log)
        {
            return;
        }

        if (log.EndTime is null)
        {
            ShowError("Eintrag läuft noch", "Laufende Zeiteinträge können erst nach dem Stoppen bearbeitet werden.");
            return;
        }

        var editor = new EditTimeLogWindow(log)
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _dbHelper.UpdateTimeLog(log.Id, editor.ClientNumber, editor.StartTime, editor.EndTime, editor.Notes);
            log.ClientNumber = editor.ClientNumber;
            log.StartTime = editor.StartTime;
            log.EndTime = editor.EndTime;
            log.Notes = editor.Notes;
            _timeLogsView.Refresh();
            UpdateResultsCount();
        }
        catch (Exception ex)
        {
            ShowError("Änderungen nicht gespeichert", ex.Message);
        }
    }

    private void NoteDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.TimeLog log ||
            string.IsNullOrWhiteSpace(log.Notes))
        {
            return;
        }

        ShowError($"Tätigkeit / Notiz · Kunde {log.ClientNumber}", log.Notes, isError: false);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.TimeLog log)
        {
            return;
        }

        var confirmation = new DeleteConfirmationWindow(log)
        {
            Owner = Window.GetWindow(this)
        };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _dbHelper.DeleteTimeLog(log.Id);
            _timeLogs.Remove(log);
            _timeLogsView.Refresh();
            UpdateResultsCount();
        }
        catch (Exception ex)
        {
            ShowError("Eintrag nicht gelöscht", ex.Message);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyCurrentFilters())
        {
            return;
        }

        List<DatabaseHelper.TimeLog> filteredLogs = _timeLogsView
            .Cast<DatabaseHelper.TimeLog>()
            .ToList();
        if (filteredLogs.Count == 0)
        {
            ShowError("Keine Einträge", "Für die aktuellen Filter gibt es keine Einträge zum Exportieren.", isError: false);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Zeiteinträge exportieren",
            Filter = "CSV-Datei (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"Zeiteinträge_{DateTime.Now:yyyyMMdd-HHmm}"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("Kundennummer;Startzeit;Endzeit;Dauer;Tätigkeit / Notiz;Abgerechnet");
            foreach (DatabaseHelper.TimeLog log in filteredLogs)
            {
                csv.Append(CsvField(log.ClientNumber ?? string.Empty)).Append(';')
                    .Append(CsvField(log.StartTime?.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("de-DE")) ?? string.Empty)).Append(';')
                    .Append(CsvField(log.EndTime?.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("de-DE")) ?? string.Empty)).Append(';')
                    .Append(CsvField(FormatDuration(log.Duration))).Append(';')
                    .Append(CsvField(log.Notes ?? string.Empty)).Append(';')
                    .AppendLine(log.IsBilled ? "Ja" : "Nein");
            }

            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ShowError("Export abgeschlossen", $"{filteredLogs.Count} Einträge wurden exportiert.", isError: false);
        }
        catch (Exception ex)
        {
            ShowError("Export fehlgeschlagen", ex.Message);
        }
    }

    private static string CsvField(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is not TimeSpan value)
        {
            return string.Empty;
        }

        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    private static string FormatSummaryDuration(TimeSpan duration)
    {
        int days = (int)duration.TotalDays;
        int hours = duration.Hours;
        int minutes = duration.Minutes;
        var parts = new List<string>();
        if (days > 0)
        {
            parts.Add($"{days} {(days == 1 ? "Tag" : "Tage")}");
        }
        if (hours > 0)
        {
            parts.Add($"{hours} Std.");
        }

        parts.Add($"{minutes} Min.");
        return string.Join(" ", parts);
    }

    private void ShowError(string title, string message, bool isError = true)
    {
        var dialog = new AppMessageWindow(title, message, isError)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
