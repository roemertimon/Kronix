using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kronix.Database;
using Microsoft.Win32;

namespace Kronix.Windows;

public partial class EinstellungenWindow : UserControl
{
    private readonly DatabaseHelper _dbHelper;

    public EinstellungenWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        _dbHelper = dbHelper;
        RefreshDatabaseInfo();
    }

    private void RefreshDatabaseInfo()
    {
        DatabasePathTextBlock.Text = _dbHelper.DatabasePath;
        DatabaseSizeTextBlock.Text = FormatBytes(_dbHelper.GetDatabaseSize());
        TimeLogCountTextBlock.Text = _dbHelper.GetTimeLogCount().ToString("N0");
        CustomerCountTextBlock.Text = _dbHelper.GetCustomerCount().ToString("N0");
        DatabaseStatusTextBlock.Text = _dbHelper.IsDatabaseInitialized()
            ? "Datenbankverbindung ist aktiv. Änderungen werden direkt in dieser Datei gespeichert."
            : $"Datenbank nicht verfügbar: {_dbHelper.InitializationError}";
    }

    private void SelectDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Datenbank auswählen",
            Filter = "SQLite-Datenbank (*.sqlite;*.db)|*.sqlite;*.db|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        RunDatabaseAction(() => _dbHelper.SwitchToDatabase(dialog.FileName),
            "Datenbank gewechselt. Die bisherige Datenbankdatei bleibt unverändert erhalten.");
    }

    private void CreateDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Neue leere Datenbank erstellen",
            Filter = "SQLite-Datenbank (*.sqlite)|*.sqlite",
            DefaultExt = ".sqlite",
            AddExtension = true,
            FileName = "Kronix.sqlite",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        RunDatabaseAction(() => _dbHelper.CreateAndSwitchToDatabase(dialog.FileName),
            "Neue leere Datenbank wurde erstellt und ist jetzt aktiv. Die bisherige Datei bleibt erhalten.");
    }

    private void CheckDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        RunDatabaseAction(() =>
        {
            string result = _dbHelper.CheckDatabaseIntegrity(repairSchema: true);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Die SQLite-Integritätsprüfung meldet: {result}");
            }
        }, "Integritätsprüfung erfolgreich. Fehlende Tabellen und Schema-Erweiterungen wurden ergänzt.");
    }

    private void ExportArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Datenbank-Sicherung als ZIP speichern",
            Filter = "ZIP-Archiv (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"Kronix-Sicherung-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        RunDatabaseAction(() => _dbHelper.ExportDatabaseArchive(dialog.FileName),
            "Die ZIP-Sicherung wurde erstellt.");
    }

    private void ImportArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new OpenFileDialog
        {
            Title = "ZIP-Sicherung auswählen",
            Filter = "ZIP-Archiv (*.zip)|*.zip",
            CheckFileExists = true
        };
        if (openDialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Wiederhergestellte Datenbank neu speichern",
            Filter = "SQLite-Datenbank (*.sqlite)|*.sqlite",
            DefaultExt = ".sqlite",
            AddExtension = true,
            FileName = $"Kronix-Wiederhergestellt-{DateTime.Now:yyyyMMdd-HHmm}",
            OverwritePrompt = false
        };
        if (saveDialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        RunDatabaseAction(() => _dbHelper.ImportDatabaseArchive(openDialog.FileName, saveDialog.FileName),
            "Sicherung wurde in eine neue Datei wiederhergestellt und ist jetzt aktiv.");
    }

    private void RunDatabaseAction(Action action, string successMessage)
    {
        try
        {
            action();
            RefreshDatabaseInfo();
            ShowMessage("Abgeschlossen", successMessage, isError: false);
        }
        catch (Exception ex)
        {
            RefreshDatabaseInfo();
            ShowMessage("Datenbankaktion fehlgeschlagen", ex.Message);
        }
    }

    private void ShowMessage(string title, string message, bool isError = true)
    {
        var dialog = new AppMessageWindow(title, message, isError)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
