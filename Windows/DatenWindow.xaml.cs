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
            _timeLogsView.Refresh();
            UpdateResultsCount();
        }
        catch (Exception ex)
        {
            ShowError("Abrechnung nicht gespeichert", ex.Message);
            LoadAllTimeLogs();
        }
    }

    private void DataGridRecords_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!DataGridRecords.IsKeyboardFocusWithin)
        {
            DataGridRecords.SelectedItem = null;
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.TimeLog log)
        {
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
            StartTime = log.StartTime,
            EndTime = log.EndTime,
            IsBilled = options.MarkExportedAsBilled || log.IsBilled,
            Notes = log.Notes
        }).ToList();

        bool exportCreated = false;
        try
        {
            if (isXlsx)
            {
                ExportToXlsx(dialog.FileName, exportLogs, options.SeparateCustomers,
                    options.IncludeSummary, options.UseDecimalHours,
                    FromDatePicker.SelectedDate, ToDatePicker.SelectedDate);
                exportCreated = true;
            }
            else
            {
                TimeLogPdfExporter.Export(
                    dialog.FileName, exportLogs, options.UseDecimalHours, options.SeparateCustomers);
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

            ShowError("Export abgeschlossen", completionMessage, isError: false);
        }
        catch (Exception ex)
        {
            ShowError(exportCreated ? "Export erstellt, aber Aktualisierung fehlgeschlagen" : "Export fehlgeschlagen",
                ex.Message);
        }
    }

    private void ExportToXlsx(
        string path,
        IReadOnlyCollection<DatabaseHelper.TimeLog> logs,
        bool separateCustomers,
        bool includeSummary,
        bool useDecimalHours,
        DateTime? fromDate,
        DateTime? toDate)
    {
        using var workbook = new XLWorkbook();
        var customerGroups = logs.GroupBy(
                log => string.IsNullOrWhiteSpace(log.ClientNumber) ? "Ohne Kundennummer" : log.ClientNumber)
            .ToList();
        var worksheetsByCustomer = new Dictionary<string, (IXLWorksheet Worksheet, int FirstDataRow, int TotalRow)>();
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
                int firstDataRow = 7;
                int totalRow = WriteWorksheet(
                    worksheet, group.ToList(), useDecimalHours, customer, includeCustomerHeader: true,
                    group.Key);
                worksheetsByCustomer[group.Key] = (worksheet, firstDataRow, totalRow);
            }
        }
        else
        {
            bool oneCustomer = customerGroups.Count == 1;
            DatabaseHelper.Customer? customer = oneCustomer
                ? GetCustomerForLogs(customerGroups[0])
                : null;
            IXLWorksheet worksheet = workbook.Worksheets.Add("Zeiteinträge");
            int firstDataRow = oneCustomer ? 7 : 2;
            int totalRow = WriteWorksheet(
                worksheet, logs, useDecimalHours, customer, includeCustomerHeader: oneCustomer,
                oneCustomer ? customerGroups[0].Key : null);
            foreach (IGrouping<string, DatabaseHelper.TimeLog> group in customerGroups)
            {
                worksheetsByCustomer[group.Key] = (worksheet, firstDataRow, totalRow);
            }
        }

        if (summaryWorksheet is not null)
        {
            WriteSummaryWorksheet(summaryWorksheet, logs, customerGroups, worksheetsByCustomer,
                separateCustomers, useDecimalHours, fromDate, toDate);
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
        DatabaseHelper.Customer? customer,
        bool includeCustomerHeader,
        string? clientNumber)
    {
        string[] headers = ["Kundennummer", "Startzeit", "Endzeit", "Dauer", "Tätigkeit / Notiz", "Abgerechnet"];
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
            if (log.StartTime is DateTime startTime)
            {
                worksheet.Cell(row, 2).Value = startTime;
                worksheet.Cell(row, 2).Style.DateFormat.Format = "dd.mm.yyyy hh:mm";
            }

            if (log.EndTime is DateTime endTime)
            {
                worksheet.Cell(row, 3).Value = endTime;
                worksheet.Cell(row, 3).Style.DateFormat.Format = "dd.mm.yyyy hh:mm";
            }

            TimeSpan? duration = GetDuration(log);
            if (duration is TimeSpan value)
            {
                worksheet.Cell(row, 4).Value = useDecimalHours
                    ? value.TotalHours
                    : value.TotalDays;
                worksheet.Cell(row, 4).Style.NumberFormat.Format = useDecimalHours ? "0.00" : "[h]:mm";
            }

            worksheet.Cell(row, 5).Value = log.Notes ?? string.Empty;
            worksheet.Cell(row, 6).Value = log.IsBilled ? "Ja" : "Nein";
            row++;
        }

        int lastDataRow = row - 1;
        int totalRow = row;
        worksheet.Cell(totalRow, 3).Value = "Gesamt";
        worksheet.Cell(totalRow, 4).FormulaA1 = lastDataRow >= firstDataRow
            ? $"=SUM(D{firstDataRow}:D{lastDataRow})"
            : "=0";
        worksheet.Cell(totalRow, 4).Style.NumberFormat.Format = useDecimalHours ? "0.00" : "[h]:mm";
        worksheet.Range(totalRow, 3, totalRow, 4).Style.Font.Bold = true;

        var header = worksheet.Range(headerRow, 1, headerRow, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF0F6");
        worksheet.Range(headerRow, 1, Math.Max(headerRow, lastDataRow), headers.Length).SetAutoFilter();
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Columns().AdjustToContents();
        worksheet.Column(5).Width = Math.Min(Math.Max(worksheet.Column(5).Width, 24), 60);
        worksheet.Column(1).Width = Math.Max(worksheet.Column(1).Width, 16);
        worksheet.Column(2).Width = Math.Max(worksheet.Column(2).Width, 19);
        worksheet.Column(3).Width = Math.Max(worksheet.Column(3).Width, 19);
        worksheet.Column(4).Width = Math.Max(worksheet.Column(4).Width, 14);
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
        IReadOnlyCollection<IGrouping<string, DatabaseHelper.TimeLog>> customerGroups,
        IReadOnlyDictionary<string, (IXLWorksheet Worksheet, int FirstDataRow, int TotalRow)> worksheetsByCustomer,
        bool separateCustomers,
        bool useDecimalHours,
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
        worksheet.Cell(3, 1).Value = "Gesamtstunden";

        string[] headers = ["Kundennummer", "Kunde / Firma", "Gesamtstunden (Dezimal)"];
        for (int column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(5, column + 1).Value = headers[column];
        }

        int row = 6;
        foreach (IGrouping<string, DatabaseHelper.TimeLog> group in customerGroups)
        {
            DatabaseHelper.Customer? customer = string.Equals(group.Key, "Ohne Kundennummer", StringComparison.Ordinal)
                ? null
                : _dbHelper.GetCustomer(group.Key);

            worksheet.Cell(row, 1).Value = group.Key;
            string customerName = customer is null
                ? string.Empty
                : string.Join(" · ", new[] { customer.Company, customer.DisplayName }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            worksheet.Cell(row, 2).Value = string.IsNullOrWhiteSpace(customerName) ? group.Key : customerName;

            (IXLWorksheet detailWorksheet, int firstDataRow, int totalRow) = worksheetsByCustomer[group.Key];
            string sheetReference = $"'{detailWorksheet.Name.Replace("'", "''")}'";
            string multiplier = useDecimalHours ? string.Empty : "*24";
            if (separateCustomers)
            {
                worksheet.Cell(row, 3).FormulaA1 = $"={sheetReference}!D{totalRow}{multiplier}";
            }
            else
            {
                int finalDataRow = totalRow - 1;
                worksheet.Cell(row, 3).FormulaA1 =
                    $"=SUMIF({sheetReference}!$A${firstDataRow}:$A${finalDataRow},A{row},{sheetReference}!$D${firstDataRow}:$D${finalDataRow}){multiplier}";
            }

            worksheet.Cell(row, 3).Style.NumberFormat.Format = "0.00";
            row++;
        }

        int lastCustomerRow = row - 1;
        worksheet.Cell(3, 2).FormulaA1 = lastCustomerRow >= 6
            ? $"=SUM(C6:C{lastCustomerRow})"
            : "=0";
        worksheet.Cell(3, 2).Style.NumberFormat.Format = "0.00";
        worksheet.Range(5, 1, 5, headers.Length).Style.Font.Bold = true;
        worksheet.Range(5, 1, 5, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF0F6");
        if (lastCustomerRow >= 5)
        {
            worksheet.Range(5, 1, lastCustomerRow, headers.Length).SetAutoFilter();
        }

        worksheet.SheetView.FreezeRows(5);
        worksheet.Columns().AdjustToContents();
        worksheet.Column(2).Width = Math.Min(Math.Max(worksheet.Column(2).Width, 24), 60);
        worksheet.Column(3).Width = Math.Max(worksheet.Column(3).Width, 26);
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
            }

            if (editor.EndTime is not DateTime endTime)
            {
                throw new InvalidOperationException("Bitte geben Sie eine gültige Endzeit an.");
            }

            _dbHelper.AddManualTimeLog(editor.ClientNumber, editor.StartTime, endTime, editor.Notes);
            LoadAllTimeLogs();
        }
        catch (Exception ex)
        {
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
