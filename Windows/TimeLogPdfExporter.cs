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
        bool separateCustomers)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Zeiteinträge";
        document.Info.Subject = "Exportierte Zeiteinträge";

        var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var headingFont = new XFont("Arial", 11, XFontStyleEx.Bold);
        var detailFont = new XFont("Arial", 10);
        var noteFont = new XFont("Arial", 9);
        var mutedFont = new XFont("Arial", 8);
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
                double entryHeight = 34 + (noteLines.Length > 0 ? 5 + noteLines.Length * LineHeight : 0);
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
                y += LineHeight + 3;

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
            : $"{(int)value.TotalHours:00}:{value.Minutes:00} h";
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
