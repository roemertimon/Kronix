using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kronix.Logging;
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
        LogPathTextBlock.Text = AppLogger.LogFilePath;
        RefreshDatabaseInfo();
    }

    private void RefreshDatabaseInfo()
    {
        DatabasePathTextBlock.Text = _dbHelper.DatabasePath;
        bool isInitialized = _dbHelper.IsDatabaseInitialized();
        DatabaseSizeTextBlock.Text = FormatBytes(_dbHelper.GetDatabaseSize());
        if (isInitialized)
        {
            TimeLogCountTextBlock.Text = _dbHelper.GetTimeLogCount().ToString("N0");
            CustomerCountTextBlock.Text = _dbHelper.GetCustomerCount().ToString("N0");
            DatabaseStatusTextBlock.Text =
                "Datenbankverbindung ist aktiv. Änderungen werden direkt in dieser Datei gespeichert.";
            DatabaseStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(40, 112, 77));
        }
        else
        {
            TimeLogCountTextBlock.Text = "–";
            CustomerCountTextBlock.Text = "–";
            DatabaseStatusTextBlock.Text =
                $"Datenbank nicht verfügbar: {_dbHelper.InitializationError ?? "Bitte Datenbank auswählen oder neu anlegen."}";
            DatabaseStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(180, 35, 24));
        }
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

        RunDatabaseAction("database.select", () => _dbHelper.SwitchToDatabase(dialog.FileName),
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

        RunDatabaseAction("database.create", () => _dbHelper.CreateAndSwitchToDatabase(dialog.FileName),
            "Neue leere Datenbank wurde erstellt und ist jetzt aktiv. Die bisherige Datei bleibt erhalten.");
    }

    private void CheckDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        RunDatabaseAction("database.integrity_check", () =>
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

        RunDatabaseAction("database.archive_export", () => _dbHelper.ExportDatabaseArchive(dialog.FileName),
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

        RunDatabaseAction("database.archive_import",
            () => _dbHelper.ImportDatabaseArchive(openDialog.FileName, saveDialog.FileName),
            "Sicherung wurde in eine neue Datei wiederhergestellt und ist jetzt aktiv.");
    }

    private void RunDatabaseAction(string eventName, Action action, string successMessage)
    {
        try
        {
            action();
            RefreshDatabaseInfo();
            AppLogger.Information(eventName, "A database settings action completed.");
            ShowMessage("Abgeschlossen", successMessage, isError: false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"{eventName}_failed",
                "A database maintenance or backup action failed.", ex);
            RefreshDatabaseInfo();
            ShowMessage("Datenbankaktion fehlgeschlagen", ex.Message);
        }
    }

    private void ViewLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Information("settings.log_view_opened", "Opened the application log viewer.");
            var viewer = new LogViewerWindow(AppLogger.LogFilePath, AppLogger.ReadCurrentLog())
            {
                Owner = Window.GetWindow(this)
            };
            viewer.ShowDialog();
        }
        catch (Exception ex)
        {
            AppLogger.Error("settings.log_view_failed", "Could not open the application log viewer.", ex);
            ShowMessage("Protokoll konnte nicht geöffnet werden", ex.Message);
        }
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? directory = Path.GetDirectoryName(AppLogger.LogFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Der Protokollordner konnte nicht ermittelt werden.");
            }

            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });

            AppLogger.Information("settings.log_folder_opened", "Opened the application log folder.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("settings.log_folder_open_failed",
                "Could not open the application log folder.", ex);
            ShowMessage("Protokollordner konnte nicht geöffnet werden", ex.Message);
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
