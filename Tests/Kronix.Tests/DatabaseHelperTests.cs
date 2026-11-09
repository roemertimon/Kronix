using Kronix.Database;
using Microsoft.Data.Sqlite;
using System.IO.Compression;

namespace Kronix.Tests;

public sealed class DatabaseHelperTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"Kronix.Tests-{Guid.NewGuid():N}");

    private const string TestEmployee = "E001";

    private readonly DatabaseHelper _database;

    public DatabaseHelperTests()
    {
        Directory.CreateDirectory(_directory);
        string databasePath = Path.Combine(_directory, "test.sqlite");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
        }

        string configurationPath = Path.Combine(_directory, "database-path.txt");
        File.WriteAllText(configurationPath, databasePath);
        _database = new DatabaseHelper(databasePathSettingPath: configurationPath);
        Assert.True(_database.IsDatabaseInitialized(), _database.InitializationError);
        _database.SaveEmployee(
            new DatabaseHelper.Employee { EmployeeNumber = TestEmployee, FirstName = "Test", LastName = "Employee" },
            makeCurrent: true);
    }

    [Fact]
    public void AddManualTimeLog_RoundsEndTimeUpToFiveMinuteIncrement()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        DateTime end = start.AddMinutes(12);

        int id = _database.AddManualTimeLog("1001", start, end, "Test entry", TestEmployee);

        DatabaseHelper.TimeLog log = Assert.Single(_database.GetAllTimeLogs());
        Assert.Equal(id, log.Id);
        Assert.Equal(start, log.StartTime);
        Assert.Equal(start.AddMinutes(15), log.EndTime);
        Assert.Equal("Test entry", log.Notes);
    }

    [Fact]
    public void GetNoteSuggestions_ReturnsDistinctSingleLineNotesByFrequency()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        _database.AddManualTimeLog("1001", start, start.AddHours(1), "Buchen", TestEmployee);
        _database.AddManualTimeLog("1001", start.AddHours(2), start.AddHours(3), "Buchhaltung", TestEmployee);
        _database.AddManualTimeLog("1001", start.AddHours(4), start.AddHours(5), "buchhaltung ", TestEmployee);
        _database.AddManualTimeLog("1001", start.AddHours(6), start.AddHours(7), "a\nb", TestEmployee);

        Assert.Equal(new[] { "buchhaltung", "Buchen" }, _database.GetNoteSuggestions());
    }

    [Fact]
    public void TwoWorkstations_ShareDatabaseButKeepOwnEmployeeAndTimers()
    {
        _database.SaveEmployee(
            new DatabaseHelper.Employee { EmployeeNumber = "E2", FirstName = "Second", LastName = "Person" });
        DateTime start = DateTime.Today.AddHours(8);
        int firstLog = _database.StartTimeLog("1001", start, "PC1", TestEmployee);

        string otherDirectory = Path.Combine(_directory, "pc2");
        Directory.CreateDirectory(otherDirectory);
        File.WriteAllText(Path.Combine(otherDirectory, "database-path.txt"), _database.DatabasePath);
        var pc2 = new DatabaseHelper(databasePathSettingPath: Path.Combine(otherDirectory, "database-path.txt"));
        pc2.SetCurrentEmployeeNumber("E2");
        int secondLog = pc2.StartTimeLog("1001", start, "PC2", "E2");

        Assert.Equal(TestEmployee, _database.GetCurrentEmployeeNumber());
        Assert.Equal("E2", pc2.GetCurrentEmployeeNumber());
        Assert.Equal(firstLog, _database.GetOpenTimeLog()!.Id);
        Assert.Equal(secondLog, pc2.GetOpenTimeLog()!.Id);

        Assert.Equal(1, pc2.CloseOpenTimeLogs(DateTime.Now));
        Assert.NotNull(_database.GetOpenTimeLog());
    }

    [Fact]
    public void SameEmployee_CanTrackIndependentlyAndStartupRecoversOnlyThisWorkstation()
    {
        string otherDirectory = Path.Combine(_directory, "pc2");
        Directory.CreateDirectory(otherDirectory);
        string otherConfigurationPath = Path.Combine(otherDirectory, "database-path.txt");
        File.WriteAllText(otherConfigurationPath, _database.DatabasePath);
        var pc2 = new DatabaseHelper(
            databasePathSettingPath: otherConfigurationPath,
            workstationId: "workstation-two");
        pc2.SetCurrentEmployeeNumber(TestEmployee);

        DateTime start = DateTime.Today.AddHours(8);
        int firstLog = _database.StartTimeLog("1001", start, "PC1", TestEmployee);
        int secondLog = pc2.StartTimeLog("1001", start, "PC2", TestEmployee);

        Assert.Equal(firstLog, _database.GetOpenTimeLog()!.Id);
        Assert.Equal(secondLog, pc2.GetOpenTimeLog()!.Id);

        var restartedPc2 = new DatabaseHelper(
            databasePathSettingPath: otherConfigurationPath,
            workstationId: "workstation-two");
        Dictionary<int, DatabaseHelper.TimeLog> logs = _database.GetAllTimeLogs().ToDictionary(log => log.Id);

        Assert.Null(logs[firstLog].EndTime);
        Assert.NotNull(logs[secondLog].EndTime);
        Assert.Null(restartedPc2.GetOpenTimeLog());
    }

    [Fact]
    public void BundledSeedDatabase_IsMigratedByStartup()
    {
        string seedPath = Path.Combine(AppContext.BaseDirectory, "Resources", "kundenDaten.sqlite");
        Assert.True(File.Exists(seedPath), "The bundled seed database was not copied to the test output.");
        string copyPath = Path.Combine(_directory, "seed-copy.sqlite");
        File.Copy(seedPath, copyPath);
        string configurationPath = Path.Combine(_directory, "seed-path.txt");
        File.WriteAllText(configurationPath, copyPath);

        var seeded = new DatabaseHelper(databasePathSettingPath: configurationPath);

        Assert.True(seeded.IsDatabaseInitialized(), seeded.InitializationError);
        Assert.Empty(seeded.GetAllTimeLogs());
        Assert.Empty(seeded.GetAllCustomers());
        Assert.Empty(seeded.GetAllEmployees());
    }

    [Fact]
    public void UpdateTimeLogsBilled_UpdatesAllSelectedEntries()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        int firstId = _database.AddManualTimeLog("1001", start, start.AddMinutes(10), null, TestEmployee);
        int secondId = _database.AddManualTimeLog("1001", start.AddHours(1), start.AddHours(1).AddMinutes(10), null, TestEmployee);

        _database.UpdateTimeLogsBilled([firstId, secondId], true);

        Assert.All(_database.GetAllTimeLogs(), log => Assert.True(log.IsBilled));
    }

    [Fact]
    public void UpdateTimeLogsBilled_RollsBackWhenAnyEntryIsMissing()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        int id = _database.AddManualTimeLog("1001", start, start.AddMinutes(10), null, TestEmployee);

        Assert.Throws<InvalidOperationException>(() =>
            _database.UpdateTimeLogsBilled([id, int.MaxValue], true));

        Assert.False(Assert.Single(_database.GetAllTimeLogs()).IsBilled);
    }

    [Fact]
    public void DeleteTimeLogs_RollsBackWhenAnyEntryIsMissing()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        int id = _database.AddManualTimeLog("1001", start, start.AddMinutes(10), null, TestEmployee);

        Assert.Throws<InvalidOperationException>(() => _database.DeleteTimeLogs([id, int.MaxValue]));

        Assert.Single(_database.GetAllTimeLogs());
    }

    [Fact]
    public void DeleteCustomer_RejectsCustomerWithTimeEntries()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        _database.SaveCustomer(new DatabaseHelper.Customer { ClientNumber = "1001" });
        _database.AddManualTimeLog("1001", start, start.AddMinutes(10), null, TestEmployee);

        Assert.Throws<InvalidOperationException>(() => _database.DeleteCustomer("1001"));

        Assert.NotNull(_database.GetCustomer("1001"));
    }

    [Fact]
    public void Startup_MigratesLegacyTimeLogColumnsAndPreservesRows()
    {
        string legacyDatabasePath = Path.Combine(_directory, "legacy.sqlite");
        using (var connection = new SqliteConnection($"Data Source={legacyDatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  CREATE TABLE TimeLogs (
                                      Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                      ClientNumber TEXT,
                                      StartTime TEXT,
                                      EndTime TEXT
                                  );
                                  INSERT INTO TimeLogs (ClientNumber, StartTime, EndTime)
                                  VALUES ('2002', '2025-01-02 09:00:00', '2025-01-02 09:15:00');
                                  """;
            command.ExecuteNonQuery();
        }

        string configurationPath = Path.Combine(_directory, "legacy-path.txt");
        File.WriteAllText(configurationPath, legacyDatabasePath);
        var migratedDatabase = new DatabaseHelper(databasePathSettingPath: configurationPath);

        Assert.True(migratedDatabase.IsDatabaseInitialized(), migratedDatabase.InitializationError);
        DatabaseHelper.TimeLog log = Assert.Single(migratedDatabase.GetAllTimeLogs());
        Assert.Equal("2002", log.ClientNumber);
        Assert.False(log.IsBilled);
        Assert.Null(log.Notes);
        Assert.NotNull(migratedDatabase.GetCustomer("2002"));
    }

    [Fact]
    public void Startup_MigratesPreEmployeeDatabaseAndKeepsAllData()
    {
        string oldPath = Path.Combine(_directory, "pre-employee.sqlite");
        using (var connection = new SqliteConnection($"Data Source={oldPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  CREATE TABLE TimeLogs (
                                      Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                      ClientNumber TEXT NOT NULL,
                                      StartTime TEXT, EndTime TEXT,
                                      IsBilled INTEGER NOT NULL DEFAULT 0,
                                      Notes TEXT
                                  );
                                  CREATE TABLE Customers (
                                      ClientNumber TEXT PRIMARY KEY NOT NULL,
                                      FirstName TEXT NOT NULL DEFAULT '', LastName TEXT NOT NULL DEFAULT '',
                                      Company TEXT NOT NULL DEFAULT '', Street TEXT NOT NULL DEFAULT '',
                                      PostalCode TEXT NOT NULL DEFAULT '', City TEXT NOT NULL DEFAULT '',
                                      PhoneNumber TEXT NOT NULL DEFAULT '', HourlyRate REAL
                                  );
                                  INSERT INTO Customers (ClientNumber, Company, HourlyRate) VALUES ('3003', 'Acme', 85.5);
                                  INSERT INTO TimeLogs (ClientNumber, StartTime, EndTime, IsBilled, Notes)
                                  VALUES ('3003', '2026-10-10 08:00:00', '2026-10-10 10:00:00', 1, 'old note');
                                  """;
            command.ExecuteNonQuery();
        }

        string configurationPath = Path.Combine(_directory, "pre-employee-path.txt");
        File.WriteAllText(configurationPath, oldPath);
        var migrated = new DatabaseHelper(databasePathSettingPath: configurationPath);

        Assert.True(migrated.IsDatabaseInitialized(), migrated.InitializationError);
        DatabaseHelper.TimeLog log = Assert.Single(migrated.GetAllTimeLogs());
        Assert.True(log.IsBilled);
        Assert.Equal("old note", log.Notes);
        Assert.Null(log.EmployeeNumber);
        Assert.Equal(85.5m, migrated.GetCustomer("3003")!.HourlyRate);
    }

    [Fact]
    public void Startup_ClosesAnUnfinishedTimeLog()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        int id = _database.StartTimeLog("1001", start, "Recovered entry", TestEmployee);
        string configurationPath = Path.Combine(_directory, "database-path.txt");

        var recoveredDatabase = new DatabaseHelper(databasePathSettingPath: configurationPath);

        DatabaseHelper.TimeLog log = Assert.Single(recoveredDatabase.GetAllTimeLogs());
        Assert.Equal(id, log.Id);
        Assert.Equal(start, log.StartTime);
        Assert.NotNull(log.EndTime);
        Assert.Equal(0, (log.EndTime.Value - start).TotalMinutes % 5);
        Assert.True(log.EndTime.Value <= DateTime.Now.AddMinutes(5));
        Assert.Equal("Recovered entry", log.Notes);
    }

    [Fact]
    public void OpenTimerDuration_UsesUtcAcrossSpringForward()
    {
        var startInstant = new DateTimeOffset(2026, 3, 29, 1, 55, 0, TimeSpan.FromHours(1));
        var currentInstant = new DateTimeOffset(2026, 3, 29, 3, 5, 0, TimeSpan.FromHours(2));
        int id = _database.StartTimeLog(
            "1001", new DateTime(2026, 3, 29, 1, 55, 0), "DST", TestEmployee, startInstant);

        DatabaseHelper.TimeLog openLog = _database.GetAllTimeLogs().Single(log => log.Id == id);

        Assert.Equal(TimeSpan.FromMinutes(10), openLog.GetOpenDuration(currentInstant));
    }

    [Fact]
    public void StopTimer_UsesUtcAcrossRepeatedFallBackHour()
    {
        var startInstant = new DateTimeOffset(2026, 10, 25, 2, 50, 0, TimeSpan.FromHours(2));
        var endInstant = new DateTimeOffset(2026, 10, 25, 2, 10, 0, TimeSpan.FromHours(1));
        int id = _database.StartTimeLog(
            "1001", new DateTime(2026, 10, 25, 2, 50, 0), "DST", TestEmployee, startInstant);

        Assert.True(_database.StopTimeLog(
            id, new DateTime(2026, 10, 25, 2, 10, 0), endInstant));

        DatabaseHelper.TimeLog log = _database.GetAllTimeLogs().Single(timeLog => timeLog.Id == id);
        Assert.Equal(TimeSpan.FromMinutes(20), log.Duration);
        Assert.Equal(startInstant.UtcDateTime, log.StartTimeUtc);
        Assert.Equal(endInstant.UtcDateTime, log.EndTimeUtc);
    }

    [Fact]
    public void StartupRecovery_UsesUtcAcrossRepeatedFallBackHour()
    {
        var startInstant = new DateTimeOffset(2026, 10, 25, 2, 50, 0, TimeSpan.FromHours(2));
        var endInstant = new DateTimeOffset(2026, 10, 25, 2, 10, 0, TimeSpan.FromHours(1));
        int id = _database.StartTimeLog(
            "1001", new DateTime(2026, 10, 25, 2, 50, 0), "DST recovery", TestEmployee, startInstant);

        Assert.Equal(1, _database.CloseOpenTimeLogs(
            new DateTime(2026, 10, 25, 2, 10, 0), endInstant));

        DatabaseHelper.TimeLog log = _database.GetAllTimeLogs().Single(timeLog => timeLog.Id == id);
        Assert.Equal(TimeSpan.FromMinutes(20), log.Duration);
    }

    [Fact]
    public void CreateAndSwitchToDatabase_CreatesAnEmptyReadyDatabase()
    {
        string newDatabasePath = Path.Combine(_directory, "new.sqlite");

        _database.CreateAndSwitchToDatabase(newDatabasePath);

        Assert.True(File.Exists(newDatabasePath));
        Assert.Equal(Path.GetFullPath(newDatabasePath), _database.DatabasePath);
        Assert.Empty(_database.GetAllCustomers());
        Assert.Empty(_database.GetAllTimeLogs());
    }

    [Fact]
    public void ExportAndImportDatabaseArchive_RestoresDataIntoNewDatabase()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        _database.SaveCustomer(new DatabaseHelper.Customer
        {
            ClientNumber = "3003",
            FirstName = "Test"
        });
        _database.AddManualTimeLog("3003", start, start.AddMinutes(10), "Backup test", TestEmployee);
        string archivePath = Path.Combine(_directory, "backup.zip");
        string restoredPath = Path.Combine(_directory, "restored.sqlite");

        _database.ExportDatabaseArchive(archivePath);
        _database.ImportDatabaseArchive(archivePath, restoredPath);

        Assert.Equal(Path.GetFullPath(restoredPath), _database.DatabasePath);
        Assert.Equal("Test", _database.GetCustomer("3003")?.FirstName);
        Assert.Equal("Backup test", Assert.Single(_database.GetAllTimeLogs()).Notes);
    }

    [Fact]
    public void ExportDatabaseArchive_PreservesExistingArchiveWhenDatabaseIsInvalid()
    {
        string invalidDatabasePath = Path.Combine(_directory, "invalid-source.sqlite");
        File.WriteAllText(invalidDatabasePath, "not a database");
        string configurationPath = Path.Combine(_directory, "invalid-source-path.txt");
        File.WriteAllText(configurationPath, invalidDatabasePath);
        var invalidDatabase = new DatabaseHelper(databasePathSettingPath: configurationPath);

        string archivePath = Path.Combine(_directory, "existing-backup.zip");
        byte[] existingArchive = [1, 2, 3, 4];
        File.WriteAllBytes(archivePath, existingArchive);

        Assert.Throws<SqliteException>(() => invalidDatabase.ExportDatabaseArchive(archivePath));

        Assert.Equal(existingArchive, File.ReadAllBytes(archivePath));
        Assert.Empty(Directory.GetFiles(_directory, ".existing-backup.zip.*.tmp"));
    }

    [Fact]
    public void ImportDatabaseArchive_RejectsMultipleDatabasesWithoutCreatingDestination()
    {
        string archivePath = Path.Combine(_directory, "ambiguous.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("first.sqlite");
            archive.CreateEntry("second.db");
        }

        string destinationPath = Path.Combine(_directory, "destination.sqlite");
        string originalPath = _database.DatabasePath;

        Assert.Throws<InvalidDataException>(() => _database.ImportDatabaseArchive(archivePath, destinationPath));

        Assert.False(File.Exists(destinationPath));
        Assert.Equal(originalPath, _database.DatabasePath);
    }

    [Fact]
    public void ImportDatabaseArchive_DeletesInvalidDatabaseAndKeepsCurrentSelection()
    {
        string archivePath = Path.Combine(_directory, "invalid.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        using (StreamWriter writer = new StreamWriter(archive.CreateEntry("invalid.sqlite").Open()))
        {
            writer.Write("not a database");
        }

        string destinationPath = Path.Combine(_directory, "invalid-destination.sqlite");
        string originalPath = _database.DatabasePath;

        Assert.Throws<SqliteException>(() => _database.ImportDatabaseArchive(archivePath, destinationPath));

        Assert.False(File.Exists(destinationPath));
        Assert.Equal(originalPath, _database.DatabasePath);
    }

    [Fact]
    public void AddManualTimeLog_AcceptsEndTimeWithinRoundingWindow_AndRejectsFarFuture()
    {
        DateTime start = DateTime.Now.AddMinutes(-10);
        DateTime endWithinWindow = DateTime.Now.AddMinutes(2);
        int id = _database.AddManualTimeLog("1001", start, endWithinWindow, "Tolerance test", TestEmployee);
        Assert.True(id > 0);

        DateTime farFuture = DateTime.Now.AddMinutes(15);
        Assert.Throws<ArgumentException>(() =>
            _database.AddManualTimeLog("1001", start, farFuture, "Invalid test", TestEmployee));
    }

    [Fact]
    public void UpdateTimeLog_AcceptsRoundedEndTime_AndRejectsFarFuture()
    {
        DateTime start = DateTime.Today.AddDays(-1).AddHours(9);
        int id = _database.AddManualTimeLog("1001", start, start.AddMinutes(15), "Initial", TestEmployee);

        DateTime roundedNow = DateTime.Now.AddMinutes(3);
        _database.UpdateTimeLog(id, "1001", DateTime.Now.AddMinutes(-5), roundedNow, "Updated");

        DateTime farFuture = DateTime.Now.AddMinutes(15);
        Assert.Throws<ArgumentException>(() =>
            _database.UpdateTimeLog(id, "1001", start, farFuture, "Far future"));
    }

    [Fact]
    public void Schema_CreatesPerformanceIndexes()
    {
        using var connection = new SqliteConnection($"Data Source={_database.DatabasePath}");
        connection.Open();
        using var command = new SqliteCommand("SELECT name FROM sqlite_master WHERE type = 'index'", connection);
        using var reader = command.ExecuteReader();
        var indexes = new List<string>();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }

        Assert.Contains("IX_Customers_ClientNumber", indexes);
        Assert.Contains("IX_TimeLogs_ClientNumber", indexes);
        Assert.Contains("IX_TimeLogs_StartTime", indexes);
        Assert.Contains("IX_TimeLogs_OpenLogs", indexes);
        Assert.Contains("IX_TimeLogs_EmployeeNumber", indexes);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}