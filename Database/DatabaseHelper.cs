using System;
namespace Kronix.Database;

using System.Data.SQLite;

public class DatabaseHelper
{
    private readonly string _connectionString;

    // Constructor
    public DatabaseHelper()
    {
        // Set the connection string to point to your SQLite database file
        string dbPath = @"P:\Software Development\C#\Kronix\Kronix\kundenDaten.sqlite";
        _connectionString = $"Data Source={dbPath};Version=3;";

        Console.WriteLine($"Database path: {dbPath}");
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        // Erstelle die Datenbank und die Tabelle, falls sie nicht existiert
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();
        const string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS TimeLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClientNumber TEXT,
                    StartTime TEXT,
                    EndTime TEXT
                )";
        SQLiteCommand command = new SQLiteCommand(createTableQuery, connection);
        command.ExecuteNonQuery();
        
        Console.WriteLine("Database initialized.");
    }

    /*
    public void InsertTimeLog(string clientNumber, DateTime startTime, DateTime endTime)
    {
        using var connection = new SQLiteConnection(ConnectionString);
        connection.Open();
        const string insertQuery = "INSERT INTO TimeLogs (ClientNumber, StartTime, EndTime) VALUES (@clientNumber, @startTime, @endTime)";
        SQLiteCommand command = new SQLiteCommand(insertQuery, connection);
        command.Parameters.AddWithValue("@clientNumber", clientNumber);
        command.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@endTime", endTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.ExecuteNonQuery();
    }*/
}
