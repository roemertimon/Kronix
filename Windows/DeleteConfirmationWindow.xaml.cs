using System;
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
