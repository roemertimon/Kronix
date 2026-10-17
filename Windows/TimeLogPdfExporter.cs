using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kronix.Database;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Kronix.Windows;

internal static class TimeLogPdfExporter
{
    private const double Margin = 40;
    private const double LineHeight = 13;

    public static void Export(
        string path,
        IReadOnlyCollection<DatabaseHelper.TimeLog> logs,
        bool useDecimalHours,
        bool separateCustomers,
        bool includeSummary,
        DateTime? fromDate,
        DateTime? toDate,
        IReadOnlyDictionary<string, DatabaseHelper.Customer> customers,
        bool includeHourlyRate)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Zeiteinträge";
        document.Info.Subject = "Exportierte Zeiteinträge";

        var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var headingFont = new XFont("Arial", 11, XFontStyleEx.Bold);
        var detailFont = new XFont("Arial", 10);
        var noteFont = new XFont("Arial", 9);
        var mutedFont = new XFont("Arial", 8);
        var summaryFont = new XFont("Arial", 10, XFontStyleEx.Bold);
        CultureInfo german = CultureInfo.GetCultureInfo("de-DE");

        List<(string CustomerNumber, List<DatabaseHelper.TimeLog> Logs)> groups = logs
            .GroupBy(log => string.IsNullOrWhiteSpace(log.ClientNumber)
                ? "Ohne Kundennummer"
                : log.ClientNumber)
            .Select(group => (group.Key, group.ToList()))
            .ToList();
        if (groups.Count == 0)
        {
            groups.Add(("Ohne Kundennummer", new List<DatabaseHelper.TimeLog>()));
        }

        XGraphics? graphics = null;
        double y = 0;
        double contentWidth = 0;
        double pageBottom = 0;
        string currentCustomer = string.Empty;
        int currentGroupCount = logs.Count;
        bool firstPage = true;

        string[] summaryHeaders = includeHourlyRate
            ? ["Kundennr.", "Kunde", "Mitarbeiter", "Eintr.", "Gesamtzeit", "€/Std.", "Betrag"]
            : ["Kundennr.", "Kunde", "Mitarbeiter", "Eintr.", "Gesamtzeit"];
        double[] summaryColumnRatios = includeHourlyRate
            ? [0.13, 0.24, 0.13, 0.10, 0.17, 0.11, 0.12]
            : [0.15, 0.32, 0.17, 0.12, 0.24];

        void DrawSummaryTableHeader()
        {
            if (graphics is null)
            {
                throw new InvalidOperationException("Die PDF-Seite konnte nicht erstellt werden.");
            }

            graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(234, 240, 246)),
                Margin, y, contentWidth, 22);
            double columnX = Margin;
            for (int column = 0; column < summaryHeaders.Length; column++)
            {
                double columnWidth = contentWidth * summaryColumnRatios[column];
                graphics.DrawString(summaryHeaders[column], mutedFont, XBrushes.DarkSlateGray,
                    new XRect(columnX + 4, y + 5, columnWidth - 6, 14), XStringFormats.TopLeft);
                columnX += columnWidth;
            }

            y += 22;
        }

        void StartSummaryPage(bool continuation = false)
        {
            graphics?.Dispose();
            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            contentWidth = page.Width.Point - Margin * 2;
            pageBottom = page.Height.Point - Margin;
            y = Margin;

            graphics.DrawString(
                continuation ? "Abrechnungsübersicht (Fortsetzung)" : "Abrechnungsübersicht",
                titleFont, XBrushes.DarkSlateBlue, new XPoint(Margin, y + titleFont.Height));
            y += titleFont.Height + 5;
            string period = FormatPeriod(logs, fromDate, toDate, german);
            graphics.DrawString(
                $"Zeitraum: {period} · Exportiert am {DateTime.Now.ToString("dd.MM.yyyy HH:mm", german)}",
                mutedFont, XBrushes.Gray, new XPoint(Margin, y + mutedFont.Height));
            y += mutedFont.Height + 15;

            TimeSpan totalDuration = SumDuration(logs);
            TimeSpan billedDuration = SumDuration(logs.Where(log => log.IsBilled));
            TimeSpan openDuration = SumDuration(logs.Where(log => !log.IsBilled));
            var metrics = new List<(string Label, string Value)>
            {
                ("Einträge", logs.Count.ToString(german)),
                ("Gesamtzeit", FormatDuration(totalDuration, useDecimalHours, german)),
                ("Abgerechnet", $"{logs.Count(log => log.IsBilled)} · {FormatDuration(billedDuration, useDecimalHours, german)}"),
                ("Offen", $"{logs.Count(log => !log.IsBilled)} · {FormatDuration(openDuration, useDecimalHours, german)}")
            };
            if (includeHourlyRate)
            {
                decimal totalAmount = logs.Sum(log => GetAmount(log, customers));
                metrics.Add(("Gesamtbetrag", totalAmount.ToString("C2", german)));
            }

            for (int index = 0; index < metrics.Count; index++)
            {
                DrawSummaryMetric(metrics[index].Label, metrics[index].Value, index, metrics.Count);
            }
            y += 65;
            DrawSummaryTableHeader();
        }

        void DrawSummaryMetric(string label, string value, int index, int metricCount)
        {
            if (graphics is null)
            {
                throw new InvalidOperationException("Die PDF-Seite konnte nicht erstellt werden.");
            }

            double gap = 8;
            double metricWidth = (contentWidth - gap * (metricCount - 1)) / metricCount;
            double x = Margin + index * (metricWidth + gap);
            graphics.DrawRoundedRectangle(new XPen(XColor.FromArgb(225, 231, 238)),
                new XSolidBrush(XColor.FromArgb(247, 249, 251)), x, y, metricWidth, 48, 5, 5);
            graphics.DrawString(label, mutedFont, XBrushes.Gray,
                new XPoint(x + 8, y + 15));
            graphics.DrawString(value, summaryFont, XBrushes.DarkSlateBlue,
                new XRect(x + 8, y + 22, metricWidth - 16, 22), XStringFormats.TopLeft);
        }

        void StartPage()
        {
            graphics?.Dispose();
            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            contentWidth = page.Width.Point - Margin * 2;
            pageBottom = page.Height.Point - Margin;
            y = Margin;

            string title = separateCustomers
                ? $"Zeiteinträge – Kunde {currentCustomer}"
                : "Zeiteinträge";
            graphics.DrawString(title, titleFont, XBrushes.DarkSlateBlue,
                new XPoint(Margin, y + titleFont.Height));
            y += titleFont.Height + 5;

            int entryCount = separateCustomers ? currentGroupCount : logs.Count;
            graphics.DrawString(
                $"{entryCount} Einträge · Exportiert am {DateTime.Now.ToString("dd.MM.yyyy HH:mm", german)}",
                mutedFont,
                XBrushes.Gray,
                new XPoint(Margin, y + mutedFont.Height));
            y += mutedFont.Height + 9;
            graphics.DrawLine(new XPen(XColor.FromArgb(210, 215, 221)), Margin, y,
                page.Width.Point - Margin, y);
            y += 12;
        }

        void EnsureSpace(double height)
        {
            if (graphics is null || y + height > pageBottom)
            {
                StartPage();
            }
        }

        if (includeSummary)
        {
            StartSummaryPage();
            var summaryGroups = logs.GroupBy(log => (
                CustomerNumber: string.IsNullOrWhiteSpace(log.ClientNumber) ? "Ohne Kundennummer" : log.ClientNumber,
                EmployeeNumber: string.IsNullOrWhiteSpace(log.EmployeeNumber) ? "—" : log.EmployeeNumber));
            foreach (IGrouping<(string CustomerNumber, string EmployeeNumber), DatabaseHelper.TimeLog> group
                     in summaryGroups)
            {
                if (graphics is null || y + 21 > pageBottom)
                {
                    StartSummaryPage(continuation: true);
                }

                if (graphics is null)
                {
                    throw new InvalidOperationException("Die PDF-Seite konnte nicht erstellt werden.");
                }

                customers.TryGetValue(group.Key.CustomerNumber, out DatabaseHelper.Customer? customer);
                string customerName = customer is null
                    ? group.Key.CustomerNumber
                    : string.Join(" · ", new[] { customer.Company, customer.DisplayName }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                TimeSpan groupDuration = SumDuration(group);
                var rowValues = new List<string>
                {
                    group.Key.CustomerNumber,
                    string.IsNullOrWhiteSpace(customerName) ? group.Key.CustomerNumber : customerName,
                    group.Key.EmployeeNumber,
                    group.Count().ToString(german),
                    FormatDuration(groupDuration, useDecimalHours, german)
                };
                if (includeHourlyRate)
                {
                    rowValues.Add(customer?.HourlyRate?.ToString("C2", german) ?? "—");
                    rowValues.Add(customer?.HourlyRate is decimal hourlyRate
                        ? group.Sum(log => GetAmount(log, customers)).ToString("C2", german)
                        : "—");
                }

                double columnX = Margin;
                for (int column = 0; column < rowValues.Count; column++)
                {
                    double columnWidth = contentWidth * summaryColumnRatios[column];
                    graphics.DrawString(rowValues[column], mutedFont, XBrushes.DarkSlateGray,
                        new XRect(columnX + 4, y + 4, columnWidth - 6, 14), XStringFormats.TopLeft);
                    columnX += columnWidth;
                }
                y += 21;
                graphics.DrawLine(new XPen(XColor.FromArgb(230, 233, 237)), Margin, y,
                    Margin + contentWidth, y);
            }

            graphics?.Dispose();
            graphics = null;
        }

        foreach ((string customerNumber, List<DatabaseHelper.TimeLog> customerLogs) in groups)
        {
            currentCustomer = customerNumber;
            currentGroupCount = customerLogs.Count;
            if (!firstPage && separateCustomers)
            {
                StartPage();
            }
            else if (graphics is null)
            {
                StartPage();
            }
            firstPage = false;

            foreach (DatabaseHelper.TimeLog log in customerLogs)
            {
                string[] noteLines = string.IsNullOrWhiteSpace(log.Notes)
                    ? Array.Empty<string>()
                    : WrapText(log.Notes, noteFont, contentWidth - 16, graphics!).ToArray();
                double entryHeight = 47 + (noteLines.Length > 0 ? 5 + noteLines.Length * LineHeight : 0);
                EnsureSpace(entryHeight);

                if (graphics is null)
                {
                    throw new InvalidOperationException("Die PDF-Seite konnte nicht erstellt werden.");
                }

                double dateColumnWidth = contentWidth * 0.30;
                double timeColumnWidth = contentWidth * 0.34;
                double durationColumnWidth = contentWidth * 0.18;
                double statusColumnWidth = contentWidth - dateColumnWidth - timeColumnWidth - durationColumnWidth;
                double x = Margin;

                graphics.DrawString(log.StartTime?.ToString("dd.MM.yyyy", german) ?? "—",
                    headingFont, XBrushes.DarkSlateGray,
                    new XRect(x, y, dateColumnWidth, LineHeight), XStringFormats.TopLeft);
                x += dateColumnWidth;
                graphics.DrawString(FormatTimeRange(log), headingFont, XBrushes.DarkSlateGray,
                    new XRect(x, y, timeColumnWidth, LineHeight), XStringFormats.TopLeft);
                x += timeColumnWidth;
                graphics.DrawString(FormatDuration(GetDuration(log), useDecimalHours, german),
                    detailFont, XBrushes.DarkSlateGray,
                    new XRect(x, y, durationColumnWidth, LineHeight), XStringFormats.TopLeft);
                x += durationColumnWidth;
                graphics.DrawString(log.IsBilled ? "Abgerechnet" : "Offen",
                    mutedFont, log.IsBilled ? XBrushes.SeaGreen : XBrushes.Gray,
                    new XRect(x, y + 1, statusColumnWidth, LineHeight), XStringFormats.TopLeft);
                string employeeDetails = $"Mitarbeiter: {log.EmployeeNumber ?? "—"}";
                if (includeHourlyRate)
                {
                    DatabaseHelper.Customer? customer = log.ClientNumber is not null &&
                        customers.TryGetValue(log.ClientNumber, out DatabaseHelper.Customer? logCustomer)
                            ? logCustomer
                            : null;
                    employeeDetails += customer?.HourlyRate is decimal hourlyRate
                        ? $" · Stundensatz: {hourlyRate.ToString("C2", german)} · Betrag: {GetAmount(log, customers).ToString("C2", german)}"
                        : " · Kein Stundensatz hinterlegt";
                }

                graphics.DrawString(employeeDetails,
                    noteFont, XBrushes.Gray,
                    new XRect(Margin, y + LineHeight + 2, contentWidth, LineHeight), XStringFormats.TopLeft);
                y += LineHeight * 2 + 3;

                foreach (string line in noteLines)
                {
                    EnsureSpace(LineHeight);
                    if (graphics is null)
                    {
                        throw new InvalidOperationException("Die PDF-Seite konnte nicht erstellt werden.");
                    }

                    graphics.DrawString(line, noteFont, XBrushes.Gray,
                        new XPoint(Margin + 8, y + noteFont.Height));
                    y += LineHeight;
                }

                y += 4;
                graphics.DrawLine(new XPen(XColor.FromArgb(230, 233, 237)), Margin, y,
                    Margin + contentWidth, y);
                y += 9;
            }
        }

        graphics?.Dispose();
        document.Save(path);
    }

    private static IEnumerable<string> WrapText(
        string text,
        XFont font,
        double maxWidth,
        XGraphics graphics)
    {
        foreach (string paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string current = string.Empty;
            foreach (string token in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string word = token;
                string candidate = current.Length == 0 ? word : $"{current} {word}";
                if (graphics.MeasureString(candidate, font).Width <= maxWidth)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0)
                {
                    yield return current;
                    current = string.Empty;
                }

                while (word.Length > 0 && graphics.MeasureString(word, font).Width > maxWidth)
                {
                    int splitAt = 1;
                    while (splitAt < word.Length &&
                           graphics.MeasureString(word[..(splitAt + 1)], font).Width <= maxWidth)
                    {
                        splitAt++;
                    }

                    yield return word[..splitAt];
                    word = word[splitAt..];
                }

                current = word;
            }

            if (current.Length > 0 || paragraph.Length == 0)
            {
                yield return current;
            }
        }
    }

    private static string FormatTimeRange(DatabaseHelper.TimeLog log)
    {
        string start = log.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
        string end = log.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "läuft";
        return $"{start}–{end}";
    }

    private static string FormatDuration(TimeSpan? duration, bool useDecimalHours, CultureInfo culture)
    {
        if (duration is not TimeSpan value)
        {
            return "—";
        }

        return useDecimalHours
            ? $"{value.TotalHours.ToString("0.00", culture)} h"
            : $"{(int)value.TotalHours:00}:{value.Minutes:00} Std.";
    }

    private static string FormatDuration(TimeSpan duration, bool useDecimalHours, CultureInfo culture)
    {
        return FormatDuration((TimeSpan?)duration, useDecimalHours, culture);
    }

    private static TimeSpan SumDuration(IEnumerable<DatabaseHelper.TimeLog> logs)
    {
        return TimeSpan.FromTicks(logs.Sum(log => GetDuration(log)?.Ticks ?? 0));
    }

    private static decimal GetAmount(
        DatabaseHelper.TimeLog log,
        IReadOnlyDictionary<string, DatabaseHelper.Customer> customers)
    {
        if (log.ClientNumber is null ||
            !customers.TryGetValue(log.ClientNumber, out DatabaseHelper.Customer? customer) ||
            customer.HourlyRate is not decimal hourlyRate ||
            GetDuration(log) is not TimeSpan duration)
        {
            return 0m;
        }

        return decimal.Round((decimal)duration.TotalHours * hourlyRate, 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatPeriod(
        IReadOnlyCollection<DatabaseHelper.TimeLog> logs,
        DateTime? fromDate,
        DateTime? toDate,
        CultureInfo culture)
    {
        DateTime? firstDate = fromDate?.Date ??
            logs.Where(log => log.StartTime.HasValue).Select(log => log.StartTime!.Value.Date)
                .DefaultIfEmpty().Min();
        DateTime? lastDate = toDate?.Date ??
            logs.Select(log => (log.EndTime ?? log.StartTime)?.Date)
                .Where(date => date.HasValue).Select(date => date!.Value)
                .DefaultIfEmpty().Max();

        if (!firstDate.HasValue || firstDate == default)
        {
            return "Keine Datumsangaben";
        }

        if (!lastDate.HasValue || lastDate == default)
        {
            lastDate = firstDate;
        }

        return firstDate == lastDate
            ? firstDate.Value.ToString("dd.MM.yyyy", culture)
            : $"{firstDate.Value.ToString("dd.MM.yyyy", culture)} – {lastDate.Value.ToString("dd.MM.yyyy", culture)}";
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
}
