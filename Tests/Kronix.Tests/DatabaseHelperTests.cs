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
        Assert.NotEmpty(seeded.GetAllTimeLogs());
        Assert.NotEmpty(seeded.GetAllCustomers());
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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}