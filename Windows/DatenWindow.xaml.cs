using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Kronix.Database;

namespace Kronix.Windows
{
    public partial class DatenWindow : UserControl
    {
        private readonly DatabaseHelper _dbHelper;
        private bool _isUpdatingDatabase = false;
        public ObservableCollection<DatabaseHelper.TimeLog> TimeLogs { get; set; }

        public DatenWindow(DatabaseHelper dbHelper)
        {
            InitializeComponent();

            // Initialisiere den DatabaseHelper
            _dbHelper = dbHelper;

            // Initialisiere die ObservableCollection
            TimeLogs = new ObservableCollection<DatabaseHelper.TimeLog>();

            // Binde die ObservableCollection an das DataGrid
            DataGridRecords.ItemsSource = TimeLogs;

            // Lade die Daten beim Start
            LoadAllTimeLogs();
        }

        private void LoadAllTimeLogs()
        {
            // Bestehende Einträge löschen
            TimeLogs.Clear();

            // Alle TimeLogs aus der Datenbank laden und der ObservableCollection hinzufügen
            var timeLogsFromDatabase = _dbHelper.GetAllTimeLogs();
            foreach (var log in timeLogsFromDatabase)
            {
                TimeLogs.Add(log);
            }
        }
        
        private void OnBilledStatusChanged(object sender, RoutedEventArgs e)
        {
            // Verhindere Endlosschleifen, wenn die Datenbank gerade aktualisiert wird
            if (_isUpdatingDatabase) return;

            if (sender is CheckBox checkBox && checkBox.DataContext is DatabaseHelper.TimeLog selectedItem)
            {
                bool newBillingStatus = checkBox.IsChecked ?? false;
                Console.WriteLine($"Checkbox für Kunde {selectedItem.ClientNumber} geändert: {newBillingStatus}");

                try
                {
                    // Schutzmechanismus aktivieren
                    _isUpdatingDatabase = true;

                    // Aktualisiere den Abrechnungsstatus in der Datenbank
                    _dbHelper.UpdateBillingStatus(selectedItem.Id, newBillingStatus);

                    // Setze den Wert nur noch im Datenmodell, ohne das Event erneut auszulösen
                    if (selectedItem.IsBilled != newBillingStatus)
                    {
                        selectedItem.IsBilled = newBillingStatus;
                    }

                    // Keine manuelle UI-Aktualisierung mehr, um Event-Schleifen zu vermeiden
                }
                finally
                {
                    // Schutzmechanismus deaktivieren
                    _isUpdatingDatabase = false;
                }
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchQuery = SearchTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                LoadAllTimeLogs(); // Zeige alle Einträge an, wenn kein Suchtext eingegeben wurde
                return;
            }

            // Filtere die TimeLogs basierend auf der ClientNumber
            var filteredLogs = _dbHelper.GetTimeLogs(searchQuery);

            TimeLogs.Clear();
            foreach (var log in filteredLogs)
            {
                TimeLogs.Add(log);
            }
        }
    }
}
