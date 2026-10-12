using System;
using System.Windows;
using Kronix.Logging;
using Kronix.Database;

namespace Kronix
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // DatabaseHelper instance that will be available throughout the application
        public static DatabaseHelper? DbHelper { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, args) =>
                AppLogger.Error("application.dispatcher_exception",
                    "An unhandled exception occurred on the UI dispatcher.", args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                AppLogger.Error("application.unhandled_exception",
                    $"An unhandled exception occurred. IsTerminating={args.IsTerminating}.",
                    args.ExceptionObject as Exception);

            AppLogger.Information("application.started", "Kronix started.");
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            CloseActiveTimeLogs();
            AppLogger.Information("application.stopped", "Kronix is shutting down.");
            base.OnExit(e);
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            AppLogger.Information("application.session_ending",
                "Windows is ending the user session.");
            CloseActiveTimeLogs();
            base.OnSessionEnding(e);
        }

        private static void CloseActiveTimeLogs()
        {
            if (DbHelper?.IsDatabaseInitialized() == true)
            {
                try
                {
                    int closedEntries = DbHelper.CloseOpenTimeLogs(DateTime.Now);
                    if (closedEntries > 0)
                    {
                        AppLogger.Warning("timelog.shutdown_closed",
                            $"Closed {closedEntries} active time log(s) during shutdown.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("timelog.shutdown_close_failed",
                        "Could not close active time logs during shutdown.", ex);
                }
            }
        }
    }
}