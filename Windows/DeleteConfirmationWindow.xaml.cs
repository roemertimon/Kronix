using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Kronix.Database;

namespace Kronix.Windows;

public partial class DeleteConfirmationWindow : Window
{
    public DeleteConfirmationWindow(DatabaseHelper.TimeLog timeLog)
    {
        InitializeComponent();
        ClientTextBlock.Text = $"Kunde {timeLog.ClientNumber}";
        StartTimeTextBlock.Text = timeLog.StartTime?.ToString("dd.MM.yyyy 'um' HH:mm") ?? "Startzeit nicht verfügbar";
    }

    public DeleteConfirmationWindow(string customerNumber)
    {
        InitializeComponent();
        TitleTextBlock.Text = "Kunde wirklich löschen?";
        DescriptionTextBlock.Text = "Möchten Sie diesen Kunden wirklich endgültig entfernen?";
        ClientTextBlock.Text = $"Kundennummer {customerNumber}";
        StartTimeTextBlock.Text = "Keine Zeiteinträge vorhanden";
        DeleteActionButton.Content = "Kunde löschen";
    }

    public DeleteConfirmationWindow(DatabaseHelper.Employee employee)
    {
        InitializeComponent();
        TitleTextBlock.Text = "Mitarbeiter wirklich löschen?";
        DescriptionTextBlock.Text = "Möchten Sie diesen Mitarbeiter wirklich endgültig entfernen?";
        ClientTextBlock.Text = $"Mitarbeiternummer {employee.EmployeeNumber} · {employee.DisplayName}";
        StartTimeTextBlock.Text = "Keine Zeiteinträge vorhanden";
        DeleteActionButton.Content = "Mitarbeiter löschen";
    }

    public DeleteConfirmationWindow(IReadOnlyCollection<DatabaseHelper.TimeLog> timeLogs)
    {
        if (timeLogs.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Zeiteintrag muss ausgewählt sein.", nameof(timeLogs));
        }

        InitializeComponent();
        TitleTextBlock.Text = "Einträge löschen?";
        DescriptionTextBlock.Text = "Möchten Sie alle markierten Zeiteinträge wirklich entfernen?";
        EntryDetailsBorder.Visibility = Visibility.Collapsed;
        BulkCountBorder.Visibility = Visibility.Visible;
        SelectedCountTextBlock.Text = timeLogs.Count.ToString();
        DeleteActionButton.Content = "Alle löschen";
        DeleteActionButton.Width = 112;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
