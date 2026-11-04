using System;
using System.Windows;
using Kronix.Logging;
using Kronix.Database;
using Kronix.Services;
using Kronix.Windows;

namespace Kronix
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // DatabaseHelper instance that will be available throughout the application
        public static DatabaseHelper? DbHelper { get; set; }

        private SingleInstanceGuard? _singleInstance;
        private bool _isDuplicateInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstance = new SingleInstanceGuard();
            if (!_singleInstance.TryAcquire())
            {
                // Another Kronix is already running: wake it up and exit quietly.
                _isDuplicateInstance = true;
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                _singleInstance.SignalExistingInstance();
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }

            _singleInstance.StartListening(() => Dispatcher.BeginInvoke(new Action(ActivateExistingWindow)));

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

        private void ActivateExistingWindow()
        {
            if (DashboardWindow.RestoreFromExternal())
            {
                return;
            }

            // Still on the start screen.
            Window? window = MainWindow;
            if (window is null)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_isDuplicateInstance)
            {
                base.OnExit(e);
                return;
            }

            CloseActiveTimeLogs();
            DashboardWindow.DisposeTray();
            _singleInstance?.Dispose();
            _singleInstance = null;
            AppLogger.Information("application.stopped", "Kronix is shutting down.");
            base.OnExit(e);
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            AppLogger.Information("application.session_ending",
                "Windows is ending the user session.");
            CloseActiveTimeLogs();
            DashboardWindow.DisposeTray();
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