using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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

        protected override void OnExit(ExitEventArgs e)
        {
            CloseActiveTimeLogs();
            base.OnExit(e);
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            CloseActiveTimeLogs();
            base.OnSessionEnding(e);
        }

        private static void CloseActiveTimeLogs()
        {
            if (DbHelper?.IsDatabaseInitialized() == true)
            {
                try
                {
                    DbHelper.CloseOpenTimeLogs(DateTime.Now);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to close active time logs during shutdown: " + ex.Message);
                }
            }
        }
    }
}