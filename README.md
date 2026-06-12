# Kronix

**Kronix** is a Windows desktop application for tracking working time and managing customer records. It combines a customer directory, a timer, searchable time-entry history, billing status, and Excel/PDF exports in one WPF app.

> The application interface is currently in German.

## Features

- **Track time by customer** — start, pause, resume, and stop a timer; add notes to an entry.
- **Manage customers** — store customer numbers, names, company names, addresses, and phone numbers.
- **Review and edit time entries** — add entries manually, edit dates and times separately, and round times to five-minute increments.
- **Filter and summarize** — filter entries by customer number, date range, and billing status; review entry counts and duration totals.
- **Manage billing status** — mark entries individually or update a multi-selection together.
- **Export selected results** — create Excel workbooks (`.xlsx`) or PDF reports with optional customer separation and billing summaries.
- **Back up and restore data** — create ZIP archives of the active SQLite database and restore them to a new database file.
- **Keep an activity log** — inspect application events and errors from Settings.
- **Run from the notification area** — minimize the dashboard to the Windows notification area and reopen it from the Kronix icon.

## Getting started

### Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) with the Windows desktop targeting pack

### Build and run

Clone the repository, then run the application from its root directory:

```powershell
git clone https://github.com/roemertimon/Kronix.git
cd Kronix
dotnet run --project Kronix.csproj
```

To build without starting the application:

```powershell
dotnet build Kronix.sln
```

The .NET SDK restores the NuGet dependencies during the first build. The repository does not currently include a packaged installer; `dotnet run` is the source-based way to launch the app.

## Using Kronix

### Time tracking

Choose a customer in the time-tracking view and start the timer. You can pause and resume the current entry, add a note, adjust its start time, and stop it when the work is complete. Kronix rounds tracked end times up to a five-minute increment.

Entries can also be added manually. The editor has separate date and time fields for the start and end, plus controls for rounding to five-minute increments.

When Kronix exits, it closes any running time entry. On the next start, it also checks for unfinished entries and closes them during database recovery.

### Customers

The customer list can be searched by customer number, name, company, address, or phone number. A customer cannot be deleted while time entries are still associated with that customer.

### Time entries

The time-entry view supports filtering by:

- One or more customer numbers (separated by commas)
- Start-date range
- Billed or unbilled status

The view displays the filtered entry count and duration totals. Billing status can be changed from an entry's row, or for all selected entries at once.

| Action | Keyboard shortcut |
| --- | --- |
| Edit the selected time entry | `E` (single selection) |
| Delete selected completed time entries | `Delete` |
| Edit the selected customer | `E` |
| Delete the selected customer | `Delete` |

Delete actions ask for confirmation. A customer with existing time entries cannot be deleted. Double-clicking a customer row also opens the customer editor.

### Exporting

Export the currently filtered time entries as an Excel workbook (`.xlsx`) or PDF. Export options include:

- Separating entries by customer (separate worksheets in Excel or pages in PDF)
- Including a billing summary
- Showing durations as decimal hours or `HH:mm`
- Marking successfully exported entries as billed (off by default)

## Data and backups

Kronix stores data in a local SQLite database. By default, the active database is:

```text
%LOCALAPPDATA%\Kronix\kundenDaten.sqlite
```

On first launch, Kronix copies the bundled `Resources\kundenDaten.sqlite` database to the per-user location when available. If no bundled database is found, it creates a new database there. The active database path is saved in:

```text
%LOCALAPPDATA%\Kronix\database-path.txt
```

The database contains customer records and time entries, including notes and billing status. Use **Settings** to view the active path, select a database, create a new empty database, run an integrity/schema check, or create and restore ZIP backups. Restoring a backup creates a new database file; it does not overwrite the selected destination.

**Protect your data:** database files contain customer and time-tracking information. Keep backup archives secure and do not publish real customer data. The application log is stored separately and is designed not to include customer records or time-entry notes.

## Application logs

Logs are stored under:

```text
%LOCALAPPDATA%\Kronix\Logs\
```

The log records application events and errors. Kronix rotates the log when it reaches approximately 1 MB and keeps up to three previous log files. The log viewer and a shortcut to the log folder are available in Settings.

## Project structure

```text
Kronix/
├── Database/       SQLite access, schema management, backup and restore
├── Logging/        Application logging
├── Media/          Application media assets
├── Resources/      Icons, images, animation, and bundled database
├── Windows/        WPF views, dialogs, and export implementations
├── App.xaml        WPF application entry point and shared resources
├── Kronix.csproj   Target framework and NuGet dependencies
└── Kronix.sln      Visual Studio/Rider solution
```

## Built with

- C# and WPF on .NET 8 for Windows
- [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) for SQLite access
- [ClosedXML](https://www.nuget.org/packages/ClosedXML) for Excel export
- [PDFsharp-WPF](https://www.nuget.org/packages/PDFsharp-WPF) for PDF export
- [WpfAnimatedGif](https://www.nuget.org/packages/WpfAnimatedGif) for animated UI assets

## License

No `LICENSE` file is currently included in this repository. Please contact the repository owner for permission and licensing terms before redistributing or reusing the project.
