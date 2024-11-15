using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using Kronix.Database;

namespace Kronix.Windows;

public partial class DatenWindow : UserControl
{
    private readonly DatabaseHelper _dbHelper;
    private ObservableCollection<DatabaseHelper.TimeLog> TimeLogs { get; set; }

    public DatenWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        _dbHelper = dbHelper;
        TimeLogs = new ObservableCollection<DatabaseHelper.TimeLog>();
        DataGridRecords.ItemsSource = TimeLogs;
        LoadAllTimeLogs();
    }

    private void LoadAllTimeLogs()
    {
        // Clear the existing collection
        TimeLogs.Clear();

        // Get all time logs from the database and add them to the ObservableCollection
        List<DatabaseHelper.TimeLog> timeLogsFromDatabase = _dbHelper.GetAllTimeLogs();
        foreach (var log in timeLogsFromDatabase)
        {
            TimeLogs.Add(log);
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
}