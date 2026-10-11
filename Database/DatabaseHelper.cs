using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Kronix.Database;

public class DatabaseHelper
{
    public class Customer
    {
        public string ClientNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;

        public string DisplayName => $"{FirstName} {LastName}".Trim();
    }

    // Model class for time log entries
    public class TimeLog
    {
        public int Id { get; set; }
        public string? ClientNumber { get; set; }
        public DateTime? StartTime { get; set; }  
        public DateTime? EndTime { get; set; }
        public bool IsBilled { get; set; }
        public string? Notes { get; set; }
        
        // Berechnete Dauer zwischen StartTime und EndTime
        public TimeSpan? Duration
        {
            get
            {
                if (StartTime.HasValue && EndTime.HasValue)
                {
                    return EndTime.Value - StartTime.Value;
                }
                return null;
            }
        }
    }
    
    private string _connectionString;
    private readonly Action<string>? _reportStatus;
    private readonly string _databasePathSettingPath;
    private bool _isDatabaseInitialized;
    private bool _hasActiveTimeLog;
    public string? InitializationError { get; private set; }
    public string DatabasePath { get; private set; } = string.Empty;

    public DatabaseHelper(Action<string>? reportStatus = null, string? databasePathSettingPath = null)
    {
        _reportStatus = reportStatus;
        _databasePathSettingPath = databasePathSettingPath ?? GetDatabaseConfigurationPath();
        _reportStatus?.Invoke("Datenbankdatei wird gesucht …");
        string baseDirectory = AppContext.BaseDirectory;
        string resourceDbPath = System.IO.Path.Combine(baseDirectory, "Resources", "kundenDaten.sqlite");
        string topLevelDbPath = System.IO.Path.Combine(baseDirectory, "kundenDaten.sqlite");

        string? configuredDbPath = GetConfiguredDatabasePath(_databasePathSettingPath);
        string dbPath = !string.IsNullOrWhiteSpace(configuredDbPath) && System.IO.File.Exists(configuredDbPath)
            ? configuredDbPath
            : resourceDbPath;

        if (!System.IO.File.Exists(dbPath))
        {
            if (System.IO.File.Exists(topLevelDbPath))
            {
                dbPath = topLevelDbPath;
            }
            else
            {
                string? directory = System.IO.Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
            }
        }

        DatabasePath = System.IO.Path.GetFullPath(dbPath);
        _connectionString = BuildConnectionString(DatabasePath, SqliteOpenMode.ReadWriteCreate);

        Console.WriteLine($"Database path: {dbPath}");
        _reportStatus?.Invoke("Datenbankverbindung wird geprüft …");
        InitializeDatabase();
        if (_isDatabaseInitialized)
        {
            _reportStatus?.Invoke("Offene Zeiteinträge werden geprüft …");
            int recoveredEntries = CloseOpenTimeLogs(DateTime.Now);
            if (recoveredEntries > 0)
            {
                Console.WriteLine($"Closed {recoveredEntries} time log(s) left open by an earlier shutdown.");
            }
        }
    }
    
    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            _reportStatus?.Invoke("Tabellen werden vorbereitet …");
            EnsureSchema(connection);

            _isDatabaseInitialized = true;
            Console.WriteLine("Database connection established.");
        }
        catch (Exception ex)
        {
            _isDatabaseInitialized = false;
            InitializationError = ex.Message;
            Console.WriteLine("Failed to initialize the database: " + ex.Message);
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        const string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS TimeLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ClientNumber TEXT NOT NULL,
                        StartTime TEXT,
                        EndTime TEXT,
                        IsBilled INTEGER NOT NULL DEFAULT 0,
                        Notes TEXT
                )";
            
        using (var command = new SqliteCommand(createTableQuery, connection))
        {
            command.ExecuteNonQuery();
        }

        const string createCustomersQuery = @"
            CREATE TABLE IF NOT EXISTS Customers (
                ClientNumber TEXT PRIMARY KEY NOT NULL,
                FirstName TEXT NOT NULL DEFAULT '',
                LastName TEXT NOT NULL DEFAULT '',
                Company TEXT NOT NULL DEFAULT '',
                Street TEXT NOT NULL DEFAULT '',
                PostalCode TEXT NOT NULL DEFAULT '',
                City TEXT NOT NULL DEFAULT '',
                PhoneNumber TEXT NOT NULL DEFAULT ''
            )";
        using (var command = new SqliteCommand(createCustomersQuery, connection))
        {
            command.ExecuteNonQuery();
        }

        using (var command = new SqliteCommand(
                   "INSERT OR IGNORE INTO Customers (ClientNumber) SELECT DISTINCT ClientNumber FROM TimeLogs WHERE ClientNumber IS NOT NULL AND TRIM(ClientNumber) <> ''",
                   connection))
        {
            command.ExecuteNonQuery();
        }

        bool hasBilledColumn = false;
        bool hasNotesColumn = false;
        using (var command = new SqliteCommand("PRAGMA table_info(TimeLogs)", connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "IsBilled", StringComparison.OrdinalIgnoreCase))
                {
                    hasBilledColumn = true;
                }
                else if (string.Equals(reader.GetString(1), "Notes", StringComparison.OrdinalIgnoreCase))
                {
                    hasNotesColumn = true;
                }
            }
        }

        if (!hasBilledColumn)
        {
            using var migration = new SqliteCommand(
                "ALTER TABLE TimeLogs ADD COLUMN IsBilled INTEGER NOT NULL DEFAULT 0",
                connection);
            migration.ExecuteNonQuery();
        }

        if (!hasNotesColumn)
        {
            using var migration = new SqliteCommand(
                "ALTER TABLE TimeLogs ADD COLUMN Notes TEXT",
                connection);
            migration.ExecuteNonQuery();
        }

        Console.WriteLine("Database initialized.");
    }
    
    public bool IsDatabaseInitialized()
    {
        return _isDatabaseInitialized;
    }

    public event EventHandler? DatabaseStateChanged;

    public List<Customer> GetAllCustomers()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT ClientNumber, FirstName, LastName, Company, Street, PostalCode, City, PhoneNumber FROM Customers ORDER BY ClientNumber COLLATE NOCASE",
            connection);
        using var reader = command.ExecuteReader();
        var customers = new List<Customer>();
        while (reader.Read())
        {
            customers.Add(ReadCustomer(reader));
        }

        return customers;
    }

    public Customer? GetCustomer(string clientNumber)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT ClientNumber, FirstName, LastName, Company, Street, PostalCode, City, PhoneNumber FROM Customers WHERE ClientNumber = @number COLLATE NOCASE",
            connection);
        command.Parameters.AddWithValue("@number", clientNumber.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCustomer(reader) : null;
    }

    public int GetTimeLogCountForCustomer(string clientNumber)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT COUNT(*) FROM TimeLogs WHERE ClientNumber = @number COLLATE NOCASE",
            connection);
        command.Parameters.AddWithValue("@number", clientNumber.Trim());
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void DeleteCustomer(string clientNumber)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var countCommand = new SqliteCommand(
            "SELECT COUNT(*) FROM TimeLogs WHERE ClientNumber = @number COLLATE NOCASE",
            connection, transaction);
        countCommand.Parameters.AddWithValue("@number", clientNumber.Trim());
        int timeLogCount = Convert.ToInt32(countCommand.ExecuteScalar());
        if (timeLogCount > 0)
        {
            string entryLabel = timeLogCount == 1 ? "Zeiteintrag" : "Zeiteinträge";
            throw new InvalidOperationException(
                $"Der Kunde kann nicht gelöscht werden, da ihm {timeLogCount} {entryLabel} zugeordnet sind. Löschen Sie zuerst alle zugehörigen Zeiteinträge.");
        }

        using var deleteCommand = new SqliteCommand(
            "DELETE FROM Customers WHERE ClientNumber = @number COLLATE NOCASE",
            connection, transaction);
        deleteCommand.Parameters.AddWithValue("@number", clientNumber.Trim());
        if (deleteCommand.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Der Kunde wurde nicht gefunden.");
        }

        transaction.Commit();
    }

    public void SaveCustomer(Customer customer, string? originalClientNumber = null)
    {
        if (string.IsNullOrWhiteSpace(customer.ClientNumber))
        {
            throw new ArgumentException("Die Kundennummer darf nicht leer sein.", nameof(customer));
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        if (!string.IsNullOrWhiteSpace(originalClientNumber))
        {
            using var transaction = connection.BeginTransaction();
            using var updateCustomer = new SqliteCommand(
                @"UPDATE Customers SET ClientNumber = @number, FirstName = @firstName, LastName = @lastName,
                    Company = @company, Street = @street, PostalCode = @postalCode, City = @city, PhoneNumber = @phone
                  WHERE ClientNumber = @original",
                connection, transaction);
            AddCustomerParameters(updateCustomer, customer);
            updateCustomer.Parameters.AddWithValue("@original", originalClientNumber.Trim());
            if (updateCustomer.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("Der Kunde wurde nicht gefunden.");
            }

            using var updateLogs = new SqliteCommand(
                "UPDATE TimeLogs SET ClientNumber = @number WHERE ClientNumber = @original",
                connection, transaction);
            updateLogs.Parameters.AddWithValue("@number", customer.ClientNumber.Trim());
            updateLogs.Parameters.AddWithValue("@original", originalClientNumber.Trim());
            updateLogs.ExecuteNonQuery();
            transaction.Commit();
            return;
        }

        using var command = new SqliteCommand(
            @"INSERT INTO Customers (ClientNumber, FirstName, LastName, Company, Street, PostalCode, City, PhoneNumber)
              VALUES (@number, @firstName, @lastName, @company, @street, @postalCode, @city, @phone)",
            connection);
        AddCustomerParameters(command, customer);
        command.ExecuteNonQuery();
    }

    public int GetCustomerCount()
    {
        return GetCount("Customers");
    }

    public int GetTimeLogCount()
    {
        return GetCount("TimeLogs");
    }

    public long GetDatabaseSize()
    {
        long total = 0;
        foreach (string path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-journal" })
        {
            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
        }

        return total;
    }

    public bool HasOpenTimeLogs()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT EXISTS(SELECT 1 FROM TimeLogs WHERE EndTime IS NULL)",
            connection);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    public string CheckDatabaseIntegrity(bool repairSchema)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        if (repairSchema)
        {
            EnsureSchema(connection);
        }

        using var command = new SqliteCommand("PRAGMA integrity_check", connection);
        string result = Convert.ToString(command.ExecuteScalar()) ??
                        "Integritätsprüfung lieferte kein Ergebnis.";
        if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            _isDatabaseInitialized = true;
            InitializationError = null;
            DatabaseStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    public void CreateAndSwitchToDatabase(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new IOException("Am gewählten Speicherort existiert bereits eine Datei.");
        }

        EnsureCanSwitchDatabase();
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool createdFile = false;
        try
        {
            using (new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                createdFile = true;
            }

            using (var connection = new SqliteConnection(BuildConnectionString(fullPath, SqliteOpenMode.ReadWrite)))
            {
                connection.Open();
                EnsureSchema(connection);
            }

            SwitchToDatabase(fullPath);
        }
        catch
        {
            if (createdFile && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            throw;
        }
    }

    public void SwitchToDatabase(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Die Datenbankdatei wurde nicht gefunden.", fullPath);
        }

        EnsureCanSwitchDatabase();
        string connectionString = BuildConnectionString(fullPath, SqliteOpenMode.ReadWrite);
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            EnsureSchema(connection);
            using var command = new SqliteCommand("PRAGMA integrity_check", connection);
            string integrity = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Die Datenbankprüfung ist fehlgeschlagen: {integrity}");
            }
        }

        CloseOpenTimeLogs(connectionString, DateTime.Now);
        SaveConfiguredDatabasePath(fullPath);
        _connectionString = connectionString;
        DatabasePath = fullPath;
        _isDatabaseInitialized = true;
        _hasActiveTimeLog = false;
        InitializationError = null;
        DatabaseStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExportDatabaseArchive(string archivePath)
    {
        string tempDatabasePath = Path.Combine(Path.GetTempPath(), $"Kronix-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var source = new SqliteConnection(_connectionString))
            using (var destination = new SqliteConnection(
                       BuildConnectionString(tempDatabasePath, SqliteOpenMode.ReadWriteCreate, pooling: false)))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(tempDatabasePath, Path.GetFileNameWithoutExtension(archivePath) + ".sqlite");
        }
        finally
        {
            if (File.Exists(tempDatabasePath))
            {
                File.Delete(tempDatabasePath);
            }
        }
    }

    public void ImportDatabaseArchive(string archivePath, string destinationPath)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        if (File.Exists(fullPath))
        {
            throw new IOException("Am gewählten Speicherort existiert bereits eine Datei. Bitte wählen Sie einen neuen Dateinamen.");
        }

        EnsureCanSwitchDatabase();
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry? databaseEntry = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.Equals(Path.GetExtension(entry.Name), ".sqlite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(entry.Name), ".db", StringComparison.OrdinalIgnoreCase))
            {
                if (databaseEntry is not null)
                {
                    throw new InvalidDataException("Das Archiv enthält mehrere Datenbanken und ist nicht eindeutig.");
                }

                databaseEntry = entry;
            }
        }

        if (databaseEntry is null || databaseEntry.Length == 0 || databaseEntry.Length > 1_000_000_000)
        {
            throw new InvalidDataException("Das Archiv enthält keine gültige SQLite-Datenbank.");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool createdFile = false;
        try
        {
            using (Stream input = databaseEntry.Open())
            using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                createdFile = true;
                input.CopyTo(output);
            }

            SwitchToDatabase(fullPath);
        }
        catch
        {
            if (createdFile && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            throw;
        }
    }

    public TimeLog? GetOpenTimeLog()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT Id, ClientNumber, StartTime, EndTime, IsBilled, Notes FROM TimeLogs WHERE EndTime IS NULL ORDER BY StartTime DESC LIMIT 1",
            connection);
        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadTimeLog(reader) : null;
    }

    // Method to start a time log entry (only adds start time)
    public int StartTimeLog(string clientNumber, DateTime startTime, string? notes = null)
    {
        startTime = TruncateToMinute(startTime);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        const string insertQuery = "INSERT INTO TimeLogs (ClientNumber, StartTime, EndTime, Notes) VALUES (@clientNumber, @startTime, NULL, @notes)";
        using SqliteCommand command = new SqliteCommand(insertQuery, connection);
        command.Parameters.AddWithValue("@clientNumber", clientNumber);
        command.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        command.ExecuteNonQuery();
        _hasActiveTimeLog = true;

        Console.WriteLine("Start time log added for client: " + clientNumber);
        using var idCommand = new SqliteCommand("SELECT last_insert_rowid()", connection);
        return Convert.ToInt32(idCommand.ExecuteScalar());
    }

    public int AddManualTimeLog(string clientNumber, DateTime startTime, DateTime endTime, string? notes)
    {
        if (string.IsNullOrWhiteSpace(clientNumber))
        {
            throw new ArgumentException("Die Kundennummer darf nicht leer sein.", nameof(clientNumber));
        }

        startTime = TruncateToMinute(startTime);
        if (endTime < startTime)
        {
            throw new ArgumentException("Die Endzeit darf nicht vor der Startzeit liegen.", nameof(endTime));
        }

        endTime = RoundEndTime(startTime, endTime);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            @"INSERT INTO TimeLogs (ClientNumber, StartTime, EndTime, Notes)
              VALUES (@clientNumber, @startTime, @endTime, @notes)",
            connection);
        command.Parameters.AddWithValue("@clientNumber", clientNumber.Trim());
        command.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@endTime", endTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        command.ExecuteNonQuery();
        using var idCommand = new SqliteCommand("SELECT last_insert_rowid()", connection);
        return Convert.ToInt32(idCommand.ExecuteScalar());
    }

    public void UpdateOpenTimeLogStartTime(int timeLogId, DateTime startTime, string? notes = null)
    {
        if (startTime > DateTime.Now)
        {
            throw new ArgumentException("Die Startzeit darf nicht in der Zukunft liegen.", nameof(startTime));
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = new SqliteCommand(
            "UPDATE TimeLogs SET StartTime = @startTime, Notes = COALESCE(@notes, Notes) WHERE Id = @id AND EndTime IS NULL",
            connection, transaction);
        command.Parameters.AddWithValue("@id", timeLogId);
        command.Parameters.AddWithValue("@startTime", TruncateToMinute(startTime).ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Der laufende Zeiteintrag wurde nicht gefunden.");
        }

        transaction.Commit();
    }

    public bool StopTimeLog(int timeLogId, DateTime endTime)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();
        using var selectCommand = new SqliteCommand(
            "SELECT StartTime FROM TimeLogs WHERE Id = @id AND EndTime IS NULL",
            connection,
            transaction);
        selectCommand.Parameters.AddWithValue("@id", timeLogId);
        object? startValue = selectCommand.ExecuteScalar();
        if (startValue is not string startText ||
            !DateTime.TryParse(startText, out DateTime startTime))
        {
            return false;
        }

        DateTime normalizedEndTime = RoundEndTime(startTime, endTime);
        const string updateQuery = @"
            UPDATE TimeLogs 
            SET EndTime = @endTime 
            WHERE Id = @id AND EndTime IS NULL";
        using SqliteCommand command = new SqliteCommand(updateQuery, connection, transaction);
        command.Parameters.AddWithValue("@id", timeLogId);
        command.Parameters.AddWithValue("@endTime", normalizedEndTime.ToString("yyyy-MM-dd HH:mm:ss"));
        bool updated = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        if (updated)
        {
            _hasActiveTimeLog = false;
        }

        return updated;
    }

    public int CloseOpenTimeLogs(DateTime endTime)
    {
        int closedCount = CloseOpenTimeLogs(_connectionString, endTime);
        if (closedCount > 0)
        {
            _hasActiveTimeLog = false;
        }

        return closedCount;
    }

    private static int CloseOpenTimeLogs(string connectionString, DateTime endTime)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var selectCommand = new SqliteCommand(
            "SELECT Id, StartTime FROM TimeLogs WHERE EndTime IS NULL",
            connection,
            transaction);
        var openEntries = new List<(int Id, DateTime StartTime)>();
        using (var reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                if (DateTime.TryParse(reader.GetString(1), out DateTime startTime))
                {
                    openEntries.Add((reader.GetInt32(0), startTime));
                }
            }
        }

        using var updateCommand = new SqliteCommand(
            "UPDATE TimeLogs SET EndTime = @endTime WHERE Id = @id AND EndTime IS NULL",
            connection,
            transaction);
        var idParameter = updateCommand.Parameters.Add("@id", SqliteType.Integer);
        var endTimeParameter = updateCommand.Parameters.Add("@endTime", SqliteType.Text);
        int updatedCount = 0;
        foreach ((int id, DateTime startTime) in openEntries)
        {
            idParameter.Value = id;
            endTimeParameter.Value = RoundEndTime(startTime, endTime).ToString("yyyy-MM-dd HH:mm:ss");
            updatedCount += updateCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return updatedCount;
    }

    public void UpdateOpenTimeLogNotes(int timeLogId, string notes)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "UPDATE TimeLogs SET Notes = @notes WHERE Id = @id AND EndTime IS NULL",
            connection);
        command.Parameters.AddWithValue("@id", timeLogId);
        command.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim());
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Der laufende Zeiteintrag wurde nicht gefunden.");
        }
    }

    public void UpdateTimeLogBilled(int timeLogId, bool isBilled)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "UPDATE TimeLogs SET IsBilled = @isBilled WHERE Id = @id",
            connection);
        command.Parameters.AddWithValue("@id", timeLogId);
        command.Parameters.AddWithValue("@isBilled", isBilled ? 1 : 0);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException($"Time log {timeLogId} was not found.");
        }
    }

    public void MarkTimeLogsBilled(IEnumerable<int> timeLogIds)
    {
        ArgumentNullException.ThrowIfNull(timeLogIds);
        int[] ids = timeLogIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using var command = new SqliteCommand(
            "UPDATE TimeLogs SET IsBilled = 1 WHERE Id = @id",
            connection,
            transaction);
        SqliteParameter idParameter = command.Parameters.Add("@id", SqliteType.Integer);

        foreach (int id in ids)
        {
            idParameter.Value = id;
            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException($"Time log {id} was not found.");
            }
        }

        transaction.Commit();
    }

    public void UpdateTimeLog(int timeLogId, string clientNumber, DateTime startTime, DateTime endTime, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(clientNumber))
        {
            throw new ArgumentException("Die Kundennummer darf nicht leer sein.", nameof(clientNumber));
        }

        if (endTime < startTime)
        {
            throw new ArgumentException("Die Endzeit darf nicht vor der Startzeit liegen.", nameof(endTime));
        }

        startTime = TruncateToMinute(startTime);
        endTime = RoundEndTime(startTime, endTime);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            @"UPDATE TimeLogs
              SET ClientNumber = @clientNumber, StartTime = @startTime, EndTime = @endTime, Notes = @notes
              WHERE Id = @id AND EndTime IS NOT NULL",
            connection);
        command.Parameters.AddWithValue("@id", timeLogId);
        command.Parameters.AddWithValue("@clientNumber", clientNumber.Trim());
        command.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@endTime", endTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Der Eintrag wurde nicht gefunden oder die Zeiterfassung läuft noch.");
        }
    }

    public void DeleteTimeLog(int timeLogId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "DELETE FROM TimeLogs WHERE Id = @id AND EndTime IS NOT NULL",
            connection);
        command.Parameters.AddWithValue("@id", timeLogId);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Der Eintrag wurde nicht gefunden oder die Zeiterfassung läuft noch.");
        }
    }

    // Method to retrieve all time logs for a specific client
    public List<TimeLog> GetTimeLogs(string clientNumber)
    {
        var timeLogs = new List<TimeLog>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        const string selectQuery = "SELECT Id, ClientNumber, StartTime, EndTime, IsBilled, Notes FROM TimeLogs WHERE ClientNumber = @clientNumber";
        using SqliteCommand command = new SqliteCommand(selectQuery, connection);
        command.Parameters.AddWithValue("@clientNumber", clientNumber);
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            timeLogs.Add(ReadTimeLog(reader));
        }

        return timeLogs;
    }

    // Method to retrieve all time logs
    public List<TimeLog> GetAllTimeLogs()
    {
        var timeLogs = new List<TimeLog>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        const string selectQuery = "SELECT Id, ClientNumber, StartTime, EndTime, IsBilled, Notes FROM TimeLogs ORDER BY StartTime DESC";
        using SqliteCommand command = new SqliteCommand(selectQuery, connection);
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            timeLogs.Add(ReadTimeLog(reader));
        }

        return timeLogs;
    }

    private static TimeLog ReadTimeLog(SqliteDataReader reader)
    {
        return new TimeLog
        {
            Id = reader.GetInt32(0),
            ClientNumber = reader.GetString(1),
            StartTime = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
            EndTime = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
            IsBilled = reader.GetInt32(4) != 0,
            Notes = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }

    private static Customer ReadCustomer(SqliteDataReader reader)
    {
        return new Customer
        {
            ClientNumber = reader.GetString(0),
            FirstName = reader.GetString(1),
            LastName = reader.GetString(2),
            Company = reader.GetString(3),
            Street = reader.GetString(4),
            PostalCode = reader.GetString(5),
            City = reader.GetString(6),
            PhoneNumber = reader.GetString(7)
        };
    }

    private static void AddCustomerParameters(SqliteCommand command, Customer customer)
    {
        command.Parameters.AddWithValue("@number", customer.ClientNumber.Trim());
        command.Parameters.AddWithValue("@firstName", customer.FirstName.Trim());
        command.Parameters.AddWithValue("@lastName", customer.LastName.Trim());
        command.Parameters.AddWithValue("@company", customer.Company.Trim());
        command.Parameters.AddWithValue("@street", customer.Street.Trim());
        command.Parameters.AddWithValue("@postalCode", customer.PostalCode.Trim());
        command.Parameters.AddWithValue("@city", customer.City.Trim());
        command.Parameters.AddWithValue("@phone", customer.PhoneNumber.Trim());
    }

    private int GetCount(string tableName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand($"SELECT COUNT(*) FROM {tableName}", connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private void EnsureCanSwitchDatabase()
    {
        if (_hasActiveTimeLog)
        {
            throw new InvalidOperationException(
                "Bitte stoppen Sie zuerst die laufende Zeiterfassung, bevor Sie die Datenbank wechseln.");
        }
    }

    private static string BuildConnectionString(string path, SqliteOpenMode mode, bool pooling = true)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = pooling
        }.ToString();
    }

    private static string GetDatabaseConfigurationPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kronix",
            "database-path.txt");
    }

    private static string? GetConfiguredDatabasePath(string configPath)
    {
        return File.Exists(configPath) ? File.ReadAllText(configPath).Trim() : null;
    }

    private void SaveConfiguredDatabasePath(string path)
    {
        string? directory = Path.GetDirectoryName(_databasePathSettingPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Der Speicherort der Datenbankeinstellung ist ungültig.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(_databasePathSettingPath, path);
    }

    private static DateTime TruncateToMinute(DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);
    }

    private static DateTime RoundEndTime(DateTime startTime, DateTime actualEndTime)
    {
        TimeSpan elapsed = actualEndTime - startTime;
        int roundedIntervals = Math.Max(1, (int)Math.Ceiling(elapsed.TotalMinutes / 5));
        return startTime.AddMinutes(roundedIntervals * 5);
    }
}