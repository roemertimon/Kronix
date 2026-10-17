using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Kronix.Database;
using Kronix.Logging;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace Kronix.Windows;

public partial class DatenWindow : UserControl
{
    private readonly DatabaseHelper _dbHelper;
    private readonly ObservableCollection<DatabaseHelper.TimeLog> _timeLogs = new();
    private readonly ICollectionView _timeLogsView;
    private bool _isViewInitialized;

    public DatenWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        _dbHelper = dbHelper;
        _timeLogsView = CollectionViewSource.GetDefaultView(_timeLogs);
        _timeLogsView.Filter = FilterTimeLog;
        DataGridRecords.ItemsSource = _timeLogsView;
        DateTime today = DateTime.Today;
        FromDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        ToDatePicker.SelectedDate = today;
        _isViewInitialized = true;
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

    private void StatusFilter_Changed(object sender, RoutedEventArgs e)
    {
        RefreshFiltersIfValid();
    }

    private void RefreshFiltersIfValid()
    {
        if (!_isViewInitialized)
        {
            return;
        }

        if (FromDatePicker.SelectedDate is DateTime fromDate &&
            ToDatePicker.SelectedDate is DateTime toDate &&
            fromDate.Date > toDate.Date)
        {
            return;
        }

        _timeLogsView.Refresh();
        UpdateResultsCount();
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        CustomerFilterTextBox.Clear();
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        AllStatusRadioButton.IsChecked = true;
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
        bool statusMatches = AllStatusRadioButton.IsChecked == true ||
                             (OpenStatusRadioButton.IsChecked == true && !log.IsBilled) ||
                             (BilledStatusRadioButton.IsChecked == true && log.IsBilled);
        if (log.StartTime is not DateTime startTime)
        {
            return !fromDate.HasValue && !toDate.HasValue && statusMatches;
        }

        return (!fromDate.HasValue || startTime.Date >= fromDate.Value) &&
               (!toDate.HasValue || startTime.Date <= toDate.Value) &&
               statusMatches;
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
        TimeSpan openDuration = TimeSpan.FromTicks(filteredLogs
            .Where(log => !log.IsBilled)
            .Sum(log => (log.Duration ?? (log.StartTime is DateTime startTime && log.EndTime is null
                ? now - startTime
                : TimeSpan.Zero)).Ticks));
        string countText = count == 1 ? "1 Eintrag" : $"{count} Einträge";
        CultureInfo german = CultureInfo.GetCultureInfo("de-DE");
        ResultsSummaryTextBlock.Text =
            $"{countText}  |  Gesamtzeit: {FormatDuration(totalDuration)} ({totalDuration.TotalHours.ToString("N2", german)} h)" +
            $"  |  Offen: {FormatDuration(openDuration)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return $"{(int)duration.TotalHours} h {duration.Minutes:00} m";
    }

    private void BilledPillButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.TimeLog log)
        {
            return;
        }

        bool previousIsBilled = log.IsBilled;
        bool isBilled = !previousIsBilled;
        if (!DataGridRecords.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !DataGridRecords.CommitEdit(DataGridEditingUnit.Row, true))
        {
            AppLogger.Warning("timelog.billing_edit_not_committed",
                "The billed state could not be changed because the grid edit could not be committed.");
            ShowError("Abrechnung nicht geändert",
                "Die Tabellenzeile konnte nicht abgeschlossen werden. Bitte versuchen Sie es erneut.");
            return;
        }

        try
        {
            _dbHelper.UpdateTimeLogBilled(log.Id, isBilled);
            log.IsBilled = isBilled;
            _timeLogsView.Refresh();
            UpdateResultsCount();
            AppLogger.Information("timelog.billing_changed",
                $"Changed the billed state of one time entry to {isBilled}.");
        }
        catch (Exception ex)
        {
            log.IsBilled = previousIsBilled;
            AppLogger.Error("timelog.billing_update_failed",
                "Could not update the billed state of a time entry.", ex);
            ShowError("Abrechnung nicht gespeichert", ex.Message);
        }
    }

    private void DataGridRecords_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int selectedCount = DataGridRecords.SelectedItems.Count;
        Visibility bulkActionVisibility = selectedCount > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        RemoveSelectedButton.Visibility = bulkActionVisibility;
        MarkSelectedBilledButton.Visibility = bulkActionVisibility;
        MarkSelectedUnbilledButton.Visibility = bulkActionVisibility;
        RemoveSelectedButton.Content = selectedCount > 0
            ? $"Entfernen ({selectedCount})"
            : "Entfernen";
    }

    private void SetSelectedBilledButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string value ||
            !bool.TryParse(value, out bool isBilled))
        {
            return;
        }

        List<DatabaseHelper.TimeLog> selectedLogs = DataGridRecords.SelectedItems
            .Cast<DatabaseHelper.TimeLog>()
            .ToList();
        if (selectedLogs.Count < 2)
        {
            return;
        }

        try
        {
            _dbHelper.UpdateTimeLogsBilled(selectedLogs.Select(log => log.Id), isBilled);
            foreach (DatabaseHelper.TimeLog log in selectedLogs)
            {
                log.IsBilled = isBilled;
            }

            _timeLogsView.Refresh();
            UpdateResultsCount();
            AppLogger.Information("timelog.bulk_billing_changed",
                $"Set {selectedLogs.Count} selected time entries to billed={isBilled}.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("timelog.bulk_billing_change_failed",
                "Could not update billed status for selected time entries.", ex);
            ShowError("Abrechnung nicht geändert", ex.Message);
        }
        finally
        {
            DataGridRecords.Focus();
        }
    }

    private void DataGridRecords_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DataGridRecords.SelectedItems.Count > 0)
        {
            e.Handled = true;
            DeleteSelectedTimeLogs();
            return;
        }

        if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.None &&
            DataGridRecords.SelectedItems.Count == 1 &&
            DataGridRecords.SelectedItem is DatabaseHelper.TimeLog selectedLog)
        {
            e.Handled = true;
            EditTimeLog(selectedLog);
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.TimeLog log)
        {
            return;
        }

        EditTimeLog(log);
    }

    private void EditTimeLog(DatabaseHelper.TimeLog log)
    {
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
            if (editor.IsOpenEntry)
            {
                _dbHelper.UpdateOpenTimeLogStartTime(log.Id, editor.StartTime);
                log.StartTime = editor.StartTime;
            }
            else
            {
                if (editor.EndTime is not DateTime endTime)
                {
                    throw new InvalidOperationException("Bitte geben Sie eine gültige Endzeit an.");
                }

                _dbHelper.UpdateTimeLog(log.Id, editor.ClientNumber, editor.StartTime, endTime, editor.Notes);
                log.ClientNumber = editor.ClientNumber;
                log.StartTime = editor.StartTime;
                log.EndTime = endTime;
                log.Notes = editor.Notes;
            }

            _timeLogsView.Refresh();
            UpdateResultsCount();
            AppLogger.Information("timelog.updated",
                editor.IsOpenEntry ? "Updated the start time of an open time entry." : "Updated a time entry.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("timelog.update_failed", "Could not update a time entry.", ex);
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
            AppLogger.Information("timelog.deleted", "Deleted a time entry.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("timelog.delete_failed", "Could not delete a time entry.", ex);
            ShowError("Eintrag nicht gelöscht", ex.Message);
        }
    }

    private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedTimeLogs();
    }

    private void DeleteSelectedTimeLogs()
    {
        List<DatabaseHelper.TimeLog> selectedLogs = DataGridRecords.SelectedItems
            .Cast<DatabaseHelper.TimeLog>()
            .ToList();
        if (selectedLogs.Count == 0)
        {
            return;
        }

        if (selectedLogs.Any(log => log.EndTime is null))
        {
            ShowError("Laufende Einträge ausgewählt",
                "Laufende Zeiterfassungen können nicht entfernt werden. Bitte wählen Sie nur abgeschlossene Zeiteinträge aus.");
            return;
        }

        var confirmation = new DeleteConfirmationWindow(selectedLogs)
        {
            Owner = Window.GetWindow(this)
        };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _dbHelper.DeleteTimeLogs(selectedLogs.Select(log => log.Id).ToArray());
            foreach (DatabaseHelper.TimeLog log in selectedLogs)
            {
                _timeLogs.Remove(log);
            }

            _timeLogsView.Refresh();
            UpdateResultsCount();
            AppLogger.Information("timelog.bulk_deleted",
                $"Deleted {selectedLogs.Count} selected time entries.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("timelog.bulk_delete_failed",
                "Could not delete selected time entries.", ex);
            ShowError("Einträge nicht gelöscht", ex.Message);
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

        var options = new ExportOptionsWindow
        {
            Owner = Window.GetWindow(this)
        };
        if (options.ShowDialog() != true)
        {
            return;
        }

        bool isXlsx = options.IsXlsx;
        string extension = isXlsx ? ".xlsx" : ".pdf";
        string filter = isXlsx
            ? "Excel-Arbeitsmappe (*.xlsx)|*.xlsx"
            : "PDF-Datei (*.pdf)|*.pdf";
        var dialog = new SaveFileDialog
        {
            Title = "Zeiteinträge exportieren",
            Filter = filter,
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"Zeiteinträge_{DateTime.Now:yyyyMMdd-HHmm}"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        List<DatabaseHelper.TimeLog> exportLogs = filteredLogs.Select(log => new DatabaseHelper.TimeLog
        {
            Id = log.Id,
            ClientNumber = log.ClientNumber,
            EmployeeNumber = log.EmployeeNumber,
            StartTime = log.StartTime,
            EndTime = log.EndTime,
            IsBilled = options.MarkExportedAsBilled || log.IsBilled,
            Notes = log.Notes
        }).ToList();

        bool exportCreated = false;
        try
        {
            Dictionary<string, DatabaseHelper.Customer> exportCustomers = GetExportCustomers(exportLogs);
            if (isXlsx)
            {
                ExportToXlsx(dialog.FileName, exportLogs, options.SeparateCustomers,
                    options.IncludeSummary, options.UseDecimalHours, options.IncludeHourlyRate,
                    exportCustomers,
                    FromDatePicker.SelectedDate, ToDatePicker.SelectedDate);
                exportCreated = true;
            }
            else
            {
                TimeLogPdfExporter.Export(
                    dialog.FileName, exportLogs, options.UseDecimalHours,
                    options.SeparateCustomers, options.IncludeSummary,
                    FromDatePicker.SelectedDate, ToDatePicker.SelectedDate,
                    exportCustomers, options.IncludeHourlyRate);
                exportCreated = true;
            }

            if (options.MarkExportedAsBilled)
            {
                try
                {
                    _dbHelper.MarkTimeLogsBilled(filteredLogs.Select(log => log.Id));
                    foreach (DatabaseHelper.TimeLog log in filteredLogs)
                    {
                        log.IsBilled = true;
                    }
                    _timeLogsView.Refresh();
                    UpdateResultsCount();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("export.billing_mark_failed",
                        "The export succeeded, but exported entries could not be marked as billed.", ex);
                    ShowError("Export erstellt, Abrechnung nicht gespeichert",
                        $"Der Export wurde erstellt, aber die Einträge konnten nicht als abgerechnet markiert werden.\n{ex.Message}");
                    return;
                }
            }

            string completionMessage = $"{filteredLogs.Count} Einträge wurden exportiert.";
            if (options.MarkExportedAsBilled)
            {
                completionMessage += "\nAlle exportierten Einträge wurden als abgerechnet markiert.";
            }

            AppLogger.Information("export.completed",
                $"Exported {filteredLogs.Count} time entry/entries as {(isXlsx ? "XLSX" : "PDF")}.");
            ShowError("Export abgeschlossen", completionMessage, isError: false);
        }
        catch (Exception ex)
        {
            AppLogger.Error("export.failed", "Could not export time entries.", ex);
            ShowError(exportCreated ? "Export erstellt, aber Aktualisierung fehlgeschlagen" : "Export fehlgeschlagen",
                ex.Message);
        }
    }

    private Dictionary<string, DatabaseHelper.Customer> GetExportCustomers(
        IEnumerable<DatabaseHelper.TimeLog> logs)
    {
        var customers = new Dictionary<string, DatabaseHelper.Customer>(StringComparer.OrdinalIgnoreCase);
        foreach (string clientNumber in logs
                     .Select(log => log.ClientNumber)
                     .Where(number => !string.IsNullOrWhiteSpace(number))
                     .Select(number => number!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DatabaseHelper.Customer? customer = _dbHelper.GetCustomer(clientNumber);
            if (customer is null)
            {
                continue;
            }

            customers[clientNumber] = customer;
        }

        return customers;
    }

    private void ExportToXlsx(
        string path,
        IReadOnlyCollection<DatabaseHelper.TimeLog> logs,
        bool separateCustomers,
        bool includeSummary,
        bool useDecimalHours,
        bool includeHourlyRate,
        IReadOnlyDictionary<string, DatabaseHelper.Customer> customers,
        DateTime? fromDate,
        DateTime? toDate)
    {
        using var workbook = new XLWorkbook();
        var customerGroups = logs.GroupBy(
                log => string.IsNullOrWhiteSpace(log.ClientNumber) ? "Ohne Kundennummer" : log.ClientNumber)
            .ToList();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IXLWorksheet? summaryWorksheet = includeSummary
            ? workbook.Worksheets.Add("Abrechnungsübersicht")
            : null;
        if (summaryWorksheet is not null)
        {
            usedSheetNames.Add(summaryWorksheet.Name);
        }

        if (separateCustomers)
        {
            foreach (IGrouping<string, DatabaseHelper.TimeLog> group in customerGroups)
            {
                string sheetName = MakeUniqueSheetName(MakeSafeSheetName(group.Key), usedSheetNames);
                var worksheet = workbook.Worksheets.Add(sheetName);
                DatabaseHelper.Customer? customer = GetCustomerForLogs(group);
                WriteWorksheet(
                    worksheet, group.ToList(), useDecimalHours, includeHourlyRate, customer, customers,
                    includeCustomerHeader: true,
                    group.Key);
            }
        }
        else
        {
            bool oneCustomer = customerGroups.Count == 1;
            DatabaseHelper.Customer? customer = oneCustomer
                ? GetCustomerForLogs(customerGroups[0])
                : null;
            IXLWorksheet worksheet = workbook.Worksheets.Add("Zeiteinträge");
            WriteWorksheet(
                worksheet, logs, useDecimalHours, includeHourlyRate, customer, customers,
                includeCustomerHeader: oneCustomer,
                oneCustomer ? customerGroups[0].Key : null);
        }

        if (summaryWorksheet is not null)
        {
            WriteSummaryWorksheet(summaryWorksheet, logs, useDecimalHours, includeHourlyRate, customers, fromDate, toDate);
        }

        workbook.SaveAs(path);
    }

    private DatabaseHelper.Customer? GetCustomerForLogs(IEnumerable<DatabaseHelper.TimeLog> logs)
    {
        string? clientNumber = logs.Select(log => log.ClientNumber)
            .FirstOrDefault(number => !string.IsNullOrWhiteSpace(number));
        return clientNumber is null ? null : _dbHelper.GetCustomer(clientNumber);
    }

    private static int WriteWorksheet(
        IXLWorksheet worksheet,
        IEnumerable<DatabaseHelper.TimeLog> logs,
        bool useDecimalHours,
        bool includeHourlyRate,
        DatabaseHelper.Customer? customer,
        IReadOnlyDictionary<string, DatabaseHelper.Customer> customers,
        bool includeCustomerHeader,
        string? clientNumber)
    {
        string[] headers = includeHourlyRate
            ? ["Kundennummer", "Mitarbeiternummer", "Startzeit", "Endzeit", "Dauer", "Tätigkeit / Notiz", "Abgerechnet", "Stundensatz", "Betrag"]
            : ["Kundennummer", "Mitarbeiternummer", "Startzeit", "Endzeit", "Dauer", "Tätigkeit / Notiz", "Abgerechnet"];
        int headerRow = includeCustomerHeader ? 6 : 1;
        if (includeCustomerHeader)
        {
            WriteCustomerHeader(worksheet, customer, clientNumber);
        }

        for (int column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(headerRow, column + 1).Value = headers[column];
        }

        int firstDataRow = headerRow + 1;
        int row = firstDataRow;
        foreach (DatabaseHelper.TimeLog log in logs)
        {
            worksheet.Cell(row, 1).Value = log.ClientNumber ?? string.Empty;
            worksheet.Cell(row, 2).Value = log.EmployeeNumber ?? string.Empty;
            if (log.StartTime is DateTime startTime)
            {
                worksheet.Cell(row, 3).Value = startTime;
                worksheet.Cell(row, 3).Style.DateFormat.Format = "dd.mm.yyyy hh:mm";
            }

            if (log.EndTime is DateTime endTime)
            {
                worksheet.Cell(row, 4).Value = endTime;
                worksheet.Cell(row, 4).Style.DateFormat.Format = "dd.mm.yyyy hh:mm";
            }

            TimeSpan? duration = GetDuration(log);
            if (duration is TimeSpan value)
            {
                worksheet.Cell(row, 5).Value = useDecimalHours
                    ? value.TotalHours
                    : value.TotalDays;
                worksheet.Cell(row, 5).Style.NumberFormat.Format = useDecimalHours ? "0.00" : "[h]:mm";
            }

            worksheet.Cell(row, 6).Value = log.Notes ?? string.Empty;
            worksheet.Cell(row, 7).Value = log.IsBilled ? "Ja" : "Nein";
            DatabaseHelper.Customer? logCustomer = log.ClientNumber is not null &&
                customers.TryGetValue(log.ClientNumber, out DatabaseHelper.Customer? matchingCustomer)
                    ? matchingCustomer
                    : null;
            if (includeHourlyRate && logCustomer?.HourlyRate is decimal hourlyRate)
            {
                worksheet.Cell(row, 8).Value = hourlyRate;
                worksheet.Cell(row, 8).Style.NumberFormat.Format = "€ #,##0.00";
                if (duration is TimeSpan billableDuration)
                {
                    worksheet.Cell(row, 9).Value = decimal.Round(
                        (decimal)billableDuration.TotalHours * hourlyRate, 2, MidpointRounding.AwayFromZero);
                    worksheet.Cell(row, 9).Style.NumberFormat.Format = "€ #,##0.00";
                }
            }
            row++;
        }

        int lastDataRow = row - 1;
        int totalRow = row;
        worksheet.Cell(totalRow, 4).Value = "Gesamt";
        worksheet.Cell(totalRow, 5).FormulaA1 = lastDataRow >= firstDataRow
            ? $"=SUM(E{firstDataRow}:E{lastDataRow})"
            : "=0";
        worksheet.Cell(totalRow, 5).Style.NumberFormat.Format = useDecimalHours ? "0.00" : "[h]:mm";
        worksheet.Range(totalRow, 4, totalRow, 5).Style.Font.Bold = true;
        if (includeHourlyRate)
        {
            worksheet.Cell(totalRow, 8).Value = "Gesamtbetrag";
            worksheet.Cell(totalRow, 9).FormulaA1 = lastDataRow >= firstDataRow
                ? $"=SUM(I{firstDataRow}:I{lastDataRow})"
                : "=0";
            worksheet.Cell(totalRow, 9).Style.NumberFormat.Format = "€ #,##0.00";
            worksheet.Range(totalRow, 8, totalRow, 9).Style.Font.Bold = true;
        }

        var header = worksheet.Range(headerRow, 1, headerRow, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF0F6");
        worksheet.Range(headerRow, 1, Math.Max(headerRow, lastDataRow), headers.Length).SetAutoFilter();
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Columns().AdjustToContents();
        worksheet.Column(6).Width = Math.Min(Math.Max(worksheet.Column(6).Width, 24), 60);
        worksheet.Column(1).Width = Math.Max(worksheet.Column(1).Width, 16);
        worksheet.Column(2).Width = Math.Max(worksheet.Column(2).Width, 19);
        worksheet.Column(3).Width = Math.Max(worksheet.Column(3).Width, 19);
        worksheet.Column(4).Width = Math.Max(worksheet.Column(4).Width, 19);
        worksheet.Column(5).Width = Math.Max(worksheet.Column(5).Width, 14);
        if (includeHourlyRate)
        {
            worksheet.Column(8).Width = Math.Max(worksheet.Column(8).Width, 16);
            worksheet.Column(9).Width = Math.Max(worksheet.Column(9).Width, 16);
        }
        return totalRow;
    }

    private static void WriteCustomerHeader(
        IXLWorksheet worksheet,
        DatabaseHelper.Customer? customer,
        string? clientNumber)
    {
        if (customer is null)
        {
            worksheet.Cell(1, 1).Value = "Kundenstammdaten nicht verfügbar";
            worksheet.Cell(4, 1).Value = $"Kundennummer: #{clientNumber ?? "unbekannt"}";
            return;
        }

        string name = string.IsNullOrWhiteSpace(customer.Company)
            ? customer.DisplayName
            : string.IsNullOrWhiteSpace(customer.DisplayName)
                ? customer.Company
                : $"{customer.Company} · {customer.DisplayName}";
        worksheet.Cell(1, 1).Value = string.IsNullOrWhiteSpace(name) ? "Kunde" : name;
        worksheet.Cell(2, 1).Value = customer.Street;
        worksheet.Cell(3, 1).Value = string.Join(" ", new[] { customer.PostalCode, customer.City }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        worksheet.Cell(4, 1).Value = $"Kundennummer: #{customer.ClientNumber}";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 14;
        worksheet.Range(1, 1, 4, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private void WriteSummaryWorksheet(
        IXLWorksheet worksheet,
        IReadOnlyCollection<DatabaseHelper.TimeLog> logs,
        bool useDecimalHours,
        bool includeHourlyRate,
        IReadOnlyDictionary<string, DatabaseHelper.Customer> customers,
        DateTime? fromDate,
        DateTime? toDate)
    {
        DateTime[] startDates = logs.Where(log => log.StartTime.HasValue)
            .Select(log => log.StartTime!.Value.Date).ToArray();
        DateTime[] endDates = logs.Select(log => log.EndTime ?? log.StartTime)
            .Where(value => value.HasValue)
            .Select(value => value!.Value.Date).ToArray();
        DateTime? firstDate = startDates.Length == 0 ? null : startDates.Min();
        DateTime? lastDate = endDates.Length == 0 ? null : endDates.Max();

        worksheet.Cell(1, 1).Value = "Exportdatum";
        worksheet.Cell(1, 2).Value = DateTime.Now;
        worksheet.Cell(1, 2).Style.DateFormat.Format = "dd.mm.yyyy hh:mm";
        worksheet.Cell(2, 1).Value = "Zeitraum";
        DateTime? periodStart = fromDate?.Date ?? firstDate;
        DateTime? periodEnd = toDate?.Date ?? lastDate;
        worksheet.Cell(2, 2).Value = periodStart.HasValue && periodEnd.HasValue
            ? $"{periodStart.Value:dd.MM.yyyy} - {periodEnd.Value:dd.MM.yyyy}"
            : "Keine Einträge";
        worksheet.Cell(3, 1).Value = useDecimalHours ? "Gesamtstunden" : "Gesamtzeit";

        string[] headers = includeHourlyRate
            ? [
                "Kundennummer", "Kunde / Firma", "Mitarbeiternummer",
                useDecimalHours ? "Gesamtstunden (Industriestunden)" : "Gesamtzeit (Stunden:Minuten)",
                "Stundensatz", "Betrag"
            ]
            : [
                "Kundennummer", "Kunde / Firma", "Mitarbeiternummer",
                useDecimalHours ? "Gesamtstunden (Industriestunden)" : "Gesamtzeit (Stunden:Minuten)"
            ];
        for (int column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(5, column + 1).Value = headers[column];
        }

        var employeeCustomerGroups = logs.GroupBy(log => (
            CustomerNumber: string.IsNullOrWhiteSpace(log.ClientNumber) ? "Ohne Kundennummer" : log.ClientNumber,
            EmployeeNumber: string.IsNullOrWhiteSpace(log.EmployeeNumber) ? "—" : log.EmployeeNumber));
        int row = 6;
        foreach (IGrouping<(string CustomerNumber, string EmployeeNumber), DatabaseHelper.TimeLog> group
                 in employeeCustomerGroups)
        {
            DatabaseHelper.Customer? customer = string.Equals(group.Key.CustomerNumber, "Ohne Kundennummer", StringComparison.Ordinal)
                ? null
                : _dbHelper.GetCustomer(group.Key.CustomerNumber);

            worksheet.Cell(row, 1).Value = group.Key.CustomerNumber;
            string customerName = customer is null
                ? string.Empty
                : string.Join(" · ", new[] { customer.Company, customer.DisplayName }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            worksheet.Cell(row, 2).Value =
                string.IsNullOrWhiteSpace(customerName) ? group.Key.CustomerNumber : customerName;
            worksheet.Cell(row, 3).Value = group.Key.EmployeeNumber;
            TimeSpan totalDuration = TimeSpan.FromTicks(group.Sum(log => GetDuration(log)?.Ticks ?? 0));
            worksheet.Cell(row, 4).Value = useDecimalHours
                ? totalDuration.TotalHours
                : totalDuration.TotalDays;
            worksheet.Cell(row, 4).Style.NumberFormat.Format = useDecimalHours ? "0.00" : "[h]:mm";
            if (includeHourlyRate)
            {
                customers.TryGetValue(group.Key.CustomerNumber, out DatabaseHelper.Customer? rateCustomer);
                if (rateCustomer?.HourlyRate is decimal hourlyRate)
                {
                    worksheet.Cell(row, 5).Value = hourlyRate;
                    worksheet.Cell(row, 5).Style.NumberFormat.Format = "€ #,##0.00";
                    decimal amount = group.Sum(log =>
                    {
                        TimeSpan? duration = GetDuration(log);
                        return duration is TimeSpan value
                            ? decimal.Round((decimal)value.TotalHours * hourlyRate, 2, MidpointRounding.AwayFromZero)
                            : 0m;
                    });
                    worksheet.Cell(row, 6).Value = amount;
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "€ #,##0.00";
                }
            }
            row++;
        }

        int lastCustomerRow = row - 1;
        worksheet.Cell(3, 2).FormulaA1 = lastCustomerRow >= 6
            ? $"=SUM(D6:D{lastCustomerRow})"
            : "=0";
        worksheet.Cell(3, 2).Style.NumberFormat.Format = useDecimalHours ? "0.00" : "[h]:mm";
        if (includeHourlyRate)
        {
            worksheet.Cell(3, 3).Value = "Gesamtbetrag";
            worksheet.Cell(3, 4).FormulaA1 = lastCustomerRow >= 6
                ? $"=SUM(F6:F{lastCustomerRow})"
                : "=0";
            worksheet.Cell(3, 4).Style.NumberFormat.Format = "€ #,##0.00";
        }
        worksheet.Range(5, 1, 5, headers.Length).Style.Font.Bold = true;
        worksheet.Range(5, 1, 5, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF0F6");
        if (lastCustomerRow >= 5)
        {
            worksheet.Range(5, 1, lastCustomerRow, headers.Length).SetAutoFilter();
        }

        worksheet.SheetView.FreezeRows(5);
        worksheet.Columns().AdjustToContents();
        worksheet.Column(2).Width = Math.Min(Math.Max(worksheet.Column(2).Width, 24), 60);
        worksheet.Column(3).Width = Math.Max(worksheet.Column(3).Width, 19);
        worksheet.Column(4).Width = Math.Max(worksheet.Column(4).Width, 32);
        if (includeHourlyRate)
        {
            worksheet.Column(5).Width = Math.Max(worksheet.Column(5).Width, 16);
            worksheet.Column(6).Width = Math.Max(worksheet.Column(6).Width, 16);
        }
    }


    private static TimeSpan? GetDuration(DatabaseHelper.TimeLog log)
    {
        if (log.Duration is TimeSpan duration)
        {
            return duration;
        }

        return log.StartTime is DateTime startTime && log.EndTime is null
            ? DateTime.Now - startTime
            : null;
    }

    private static string MakeSafeSheetName(string name)
    {
        string sanitized = new string(name
            .Select(character => "[]:*?/\\'".Contains(character) ? '_' : character)
            .ToArray()).Trim('\'');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Kunde";
        }

        return sanitized.Length > 31 ? sanitized[..31] : sanitized;
    }

    private static string MakeUniqueSheetName(string candidate, ISet<string> usedNames)
    {
        string name = candidate;
        int suffix = 2;
        while (!usedNames.Add(name))
        {
            string suffixText = $" ({suffix++})";
            int maxBaseLength = 31 - suffixText.Length;
            name = $"{candidate[..Math.Min(candidate.Length, maxBaseLength)]}{suffixText}";
        }

        return name;
    }

    private static string MakeSafeFileName(string name)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        string sanitized = new(name.Select(character =>
            invalidCharacters.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Unbekannt" : sanitized;
    }

    private void AddManualEntryButton_Click(object sender, RoutedEventArgs e)
    {
        string? employeeNumber;
        try
        {
            employeeNumber = _dbHelper.GetCurrentEmployeeNumber();
        }
        catch (Exception ex)
        {
            AppLogger.Error("timelog.current_employee_read_failed",
                "Could not read the current employee before creating a manual time entry.", ex);
            ShowError("Mitarbeiter konnte nicht geladen werden", ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(employeeNumber))
        {
            AppLogger.Warning("timelog.manual_create_without_employee",
                "Manual time-entry creation was rejected because no current employee was selected.");
            ShowError("Kein aktueller Mitarbeiter",
                "Bitte legen Sie im Mitarbeiter-Tab zuerst einen Mitarbeiter an und wählen Sie ihn als aktuell aus.");
            return;
        }

        var editor = new EditTimeLogWindow
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (_dbHelper.GetCustomer(editor.ClientNumber) is null)
            {
                var customerEditor = new CustomerEditorWindow(requestedClientNumber: editor.ClientNumber)
                {
                    Owner = Window.GetWindow(this)
                };
                if (customerEditor.ShowDialog() != true)
                {
                    return;
                }

                _dbHelper.SaveCustomer(customerEditor.Customer);
                AppLogger.Information("customers.created", "Created a customer during manual time entry.");
            }

            if (editor.EndTime is not DateTime endTime)
            {
                throw new InvalidOperationException("Bitte geben Sie eine gültige Endzeit an.");
            }

            _dbHelper.AddManualTimeLog(editor.ClientNumber, editor.StartTime, endTime, editor.Notes,
                employeeNumber);
            LoadAllTimeLogs();
            AppLogger.Information("timelog.created_manually", "Created a time entry manually.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("timelog.manual_create_failed", "Could not create a time entry manually.", ex);
            ShowError("Eintrag nicht hinzugefügt", ex.Message);
        }
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
