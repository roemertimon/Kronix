using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Kronix.Database;

public class DatabaseHelper
{
    // Model class for time log entries
    public class TimeLog
    {
        public int Id { get; set; }
        public string? ClientNumber { get; set; }
        public DateTime? StartTime { get; set; }  
        public DateTime? EndTime { get; set; }
    }
    
    private readonly string _connectionString;
    private bool _isDatabaseInitialized;

    // Constructor
    public DatabaseHelper()
    {
        // Set the connection string to point to your SQLite database file
        string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "kundenDaten.sqlite");
        _connectionString = $"Data Source={dbPath};Version=3;";


        Console.WriteLine($"Database path: {dbPath}");
        InitializeDatabase();
    }
    
    private void InitializeDatabase()
    {
        try
        {
            // Erstelle die Datenbank und die Tabelle, falls sie nicht existiert
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            
            const string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS TimeLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ClientNumber TEXT NOT NULL,
                        StartTime TEXT,
                        EndTime TEXT
                )";
            
            SQLiteCommand command = new SQLiteCommand(createTableQuery, connection);
            command.ExecuteNonQuery();
        
            _isDatabaseInitialized = true;
            Console.WriteLine("Database initialized.");
        }
        catch (Exception ex)
        {
            _isDatabaseInitialized = false;
            Console.WriteLine("Failed to initialize the database: " + ex.Message);
        }
    }
    
    public bool IsDatabaseInitialized()
    {
        return _isDatabaseInitialized;
    }

    // Method to start a time log entry (only adds start time)
    public void StartTimeLog(string clientNumber, DateTime startTime)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();
        const string insertQuery = "INSERT INTO TimeLogs (ClientNumber, StartTime, EndTime) VALUES (@clientNumber, @startTime, NULL)";
        SQLiteCommand command = new SQLiteCommand(insertQuery, connection);
        command.Parameters.AddWithValue("@clientNumber", clientNumber);
        command.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.ExecuteNonQuery();

        Console.WriteLine("Start time log added for client: " + clientNumber);
    }

    // Method to stop a time log entry (updates the latest log with end time)
    public void StopTimeLog(string clientNumber, DateTime endTime)
    {
        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();

        const string updateQuery = @"
            UPDATE TimeLogs 
            SET EndTime = @endTime 
            WHERE Id = (
                SELECT Id FROM TimeLogs 
                WHERE ClientNumber = @clientNumber 
                AND EndTime IS NULL 
                ORDER BY StartTime DESC 
                LIMIT 1
           )";   
        SQLiteCommand command = new SQLiteCommand(updateQuery, connection);
        command.Parameters.AddWithValue("@clientNumber", clientNumber);
        command.Parameters.AddWithValue("@endTime", endTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.ExecuteNonQuery();

        Console.WriteLine("End time log updated for client: " + clientNumber);

    }

    // Method to retrieve all time logs for a specific client
    public List<TimeLog> GetTimeLogs(string clientNumber)
    {
        var timeLogs = new List<TimeLog>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();
        const string selectQuery = "SELECT Id, ClientNumber, StartTime, EndTime FROM TimeLogs WHERE ClientNumber = @clientNumber";
        SQLiteCommand command = new SQLiteCommand(selectQuery, connection);
        command.Parameters.AddWithValue("@clientNumber", clientNumber);
        using SQLiteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            var timeLog = new TimeLog
            {
                Id = reader.GetInt32(0),
                ClientNumber = reader.GetString(1),
                StartTime = reader.IsDBNull(2) ? (DateTime?)null : DateTime.Parse(reader.GetString(2)),
                EndTime = reader.IsDBNull(3) ? (DateTime?)null : DateTime.Parse(reader.GetString(3))
            };
            timeLogs.Add(timeLog);
        }

        return timeLogs;
    }

    // Method to retrieve all time logs
    public List<TimeLog> GetAllTimeLogs()
    {
        var timeLogs = new List<TimeLog>();

        using var connection = new SQLiteConnection(_connectionString);
        connection.Open();
        const string selectQuery = "SELECT Id, ClientNumber, StartTime, EndTime FROM TimeLogs";
        SQLiteCommand command = new SQLiteCommand(selectQuery, connection);
        using SQLiteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            var timeLog = new TimeLog
            {
                Id = reader.GetInt32(0),
                ClientNumber = reader.GetString(1),
                StartTime = reader.IsDBNull(2) ? (DateTime?)null : DateTime.Parse(reader.GetString(2)),
                EndTime = reader.IsDBNull(3) ? (DateTime?)null : DateTime.Parse(reader.GetString(3))
            };
            timeLogs.Add(timeLog);
        }

        return timeLogs;
    }
}