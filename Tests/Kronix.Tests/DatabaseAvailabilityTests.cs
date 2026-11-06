using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Kronix.Database;
using Kronix.Windows;
using Microsoft.Data.Sqlite;

namespace Kronix.Tests;

public sealed class DatabaseAvailabilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Kronix.Availability-{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _configurationPath;

    public DatabaseAvailabilityTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "test.sqlite");
        _configurationPath = Path.Combine(_directory, "database-path.txt");
        File.WriteAllText(_configurationPath, _databasePath);
    }

    [Fact]
    public void MissingAtStartup_DataViewsAreNotConstructed_AndSettingsRemainAvailable()
    {
        var database = new DatabaseHelper(databasePathSettingPath: _configurationPath);
        Assert.False(database.IsDatabaseInitialized());
        Assert.False(File.Exists(_databasePath));

        RunOnSta(() =>
        {
            Assert.False(database.TryRead(() => new MitarbeiterWindow(database), out _));
            Assert.False(database.TryRead(() => new KundenWindow(database), out _));
            Assert.False(database.TryRead(() => new DatenWindow(database), out _));
            Assert.NotNull(new EinstellungenWindow(database));
        });
        Assert.False(File.Exists(_databasePath));
    }

    [Fact]
    public void ReadGuard_DoesNotHideUnexpectedErrors()
    {
        using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();
        }

        var database = new DatabaseHelper(databasePathSettingPath: _configurationPath);
        Assert.True(database.IsDatabaseInitialized(), database.InitializationError);
        Assert.Throws<InvalidOperationException>(() =>
            database.TryRead<int>(() => throw new InvalidOperationException("unexpected"), out _));
        Assert.True(database.IsDatabaseInitialized());
    }

    [Theory]
    [InlineData("employees")]
    [InlineData("customers")]
    [InlineData("timeLogs")]
    [InlineData("tracking")]
    [InlineData("settingsCounts")]
    public void DatabaseDisappears_ReadFailureIsReported_AndRecoveryRestoresViews(string view)
    {
        using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();
        }

        var database = new DatabaseHelper(databasePathSettingPath: _configurationPath);
        Assert.True(database.IsDatabaseInitialized(), database.InitializationError);
        int stateChanges = 0;
        database.DatabaseStateChanged += (_, _) => stateChanges++;
        SqliteConnection.ClearAllPools();
        string movedPath = Path.Combine(_directory, "temporarily-unavailable.sqlite");
        File.Move(_databasePath, movedPath);

        RunOnSta(() =>
        {
            bool succeeded = view switch
            {
                "employees" => database.TryRead(() => new MitarbeiterWindow(database), out _),
                "customers" => database.TryRead(() => new KundenWindow(database), out _),
                "timeLogs" => database.TryRead(() => new DatenWindow(database), out _),
                "tracking" => database.TryRead(() => new Zeiterfassung(database), out _),
                "settingsCounts" => database.TryRead(() => database.GetTimeLogCount(), out _),
                _ => throw new InvalidOperationException()
            };
            Assert.False(succeeded);
            Assert.False(database.IsDatabaseInitialized());
            Assert.NotNull(database.InitializationError);
            Assert.NotNull(new EinstellungenWindow(database));
        });

        Assert.Equal(1, stateChanges);
        Assert.False(File.Exists(_databasePath));
        File.Move(movedPath, _databasePath);
        if (view == "employees")
        {
            database.SwitchToDatabase(_databasePath);
        }
        else
        {
            Assert.Equal("ok", database.CheckDatabaseIntegrity(repairSchema: true));
        }
        Assert.True(database.IsDatabaseInitialized());
        Assert.Null(database.InitializationError);
        Assert.Equal(2, stateChanges);

        RunOnSta(() =>
        {
            Assert.True(database.TryRead(() => new MitarbeiterWindow(database), out MitarbeiterWindow? employees));
            Assert.True(database.TryRead(() => new KundenWindow(database), out KundenWindow? customers));
            Assert.True(database.TryRead(() => new DatenWindow(database), out DatenWindow? logs));
            Assert.IsAssignableFrom<UserControl>(employees);
            Assert.IsAssignableFrom<UserControl>(customers);
            Assert.IsAssignableFrom<UserControl>(logs);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
