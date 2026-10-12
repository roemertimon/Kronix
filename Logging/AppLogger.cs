using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Kronix.Logging;

public static class AppLogger
{
    private const long MaximumFileSize = 1_000_000;
    private const int RetainedArchives = 3;
    private const int MaximumMessageLength = 12_000;
    private static readonly object Sync = new();
    private static readonly Mutex FileMutex = new(false, @"Local\Kronix.ApplicationLog");

    public static string LogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kronix",
        "Logs",
        "kronix.log");

    public static void Information(string eventName, string message) =>
        Write("Information", eventName, message, null);

    public static void Warning(string eventName, string message) =>
        Write("Warning", eventName, message, null);

    public static void Error(string eventName, string message, Exception? exception = null) =>
        Write("Error", eventName, message, exception);

    public static string ReadCurrentLog()
    {
        lock (Sync)
        {
            WaitForFileMutex();
            try
            {
                return File.Exists(LogFilePath)
                    ? File.ReadAllText(LogFilePath, Encoding.UTF8)
                    : string.Empty;
            }
            finally
            {
                FileMutex.ReleaseMutex();
            }
        }
    }

    private static void Write(string level, string eventName, string message, Exception? exception)
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The application log directory could not be determined.");
            }

            var record = new LogRecord(
                DateTimeOffset.Now,
                level,
                Limit(eventName),
                Limit(message),
                exception is null ? null : Limit(exception.ToString()));
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + Environment.NewLine);

            lock (Sync)
            {
                WaitForFileMutex();
                try
                {
                    Directory.CreateDirectory(directory);
                    RepairIncompleteFinalRecord();
                    RotateIfNeeded(bytes.Length);
                    using var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    stream.Write(bytes);
                    stream.Flush();
                }
                finally
                {
                    FileMutex.ReleaseMutex();
                }
            }
        }
        catch (Exception loggingException)
        {
            Console.Error.WriteLine($"Kronix could not write its log: {loggingException.Message}");
        }
    }

    private static void WaitForFileMutex()
    {
        try
        {
            if (!FileMutex.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out waiting for the application log file.");
            }
        }
        catch (AbandonedMutexException)
        {
        }
    }

    private static void RepairIncompleteFinalRecord()
    {
        if (!File.Exists(LogFilePath))
        {
            return;
        }

        using var stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length == 0)
        {
            return;
        }

        stream.Position = stream.Length - 1;
        if (stream.ReadByte() == (byte)'\n')
        {
            return;
        }

        long position = stream.Length - 1;
        while (position >= 0)
        {
            stream.Position = position;
            if (stream.ReadByte() == (byte)'\n')
            {
                stream.SetLength(position + 1);
                return;
            }

            position--;
        }

        stream.SetLength(0);
    }

    private static void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(LogFilePath) || new FileInfo(LogFilePath).Length + incomingBytes <= MaximumFileSize)
        {
            return;
        }

        string oldestArchive = ArchivePath(RetainedArchives);
        if (File.Exists(oldestArchive))
        {
            File.Delete(oldestArchive);
        }

        for (int index = RetainedArchives - 1; index >= 1; index--)
        {
            string archive = ArchivePath(index);
            if (File.Exists(archive))
            {
                File.Move(archive, ArchivePath(index + 1));
            }
        }

        File.Move(LogFilePath, ArchivePath(1));
    }

    private static string ArchivePath(int index) => $"{LogFilePath}.{index}";

    private static string Limit(string value) =>
        value.Length <= MaximumMessageLength ? value : value[..MaximumMessageLength] + "…";

    private sealed record LogRecord(
        DateTimeOffset Timestamp,
        string Level,
        string Event,
        string Message,
        string? Exception);
}
