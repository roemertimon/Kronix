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
        public static DatabaseHelper DbHelper { get; private set; }

        // Is called when the application starts
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize the DatabaseHelper instance
            DbHelper = new DatabaseHelper();
            
            // The MainWindow will be displayed automatically based on App.xaml settings
        }
    }
}