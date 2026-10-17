using System.Windows;
using System.Windows.Input;
using Kronix.Database;
using Kronix.Logging;

namespace Kronix.Windows;

public partial class EmployeeEditorWindow : Window
{
    private readonly string? _originalEmployeeNumber;

    public DatabaseHelper.Employee Employee { get; private set; } = new();
    public bool MakeCurrent => MakeCurrentCheckBox.IsChecked == true;
    public string? OriginalEmployeeNumber => _originalEmployeeNumber;

    public EmployeeEditorWindow(DatabaseHelper.Employee? employee = null, bool isCurrent = false)
    {
        InitializeComponent();
        if (employee is null)
        {
            MakeCurrentCheckBox.IsChecked = true;
            return;
        }

        TitleTextBlock.Text = "Mitarbeiter bearbeiten";
        SubtitleTextBlock.Text = "Mitarbeiternummer, Vorname und Nachname anpassen.";
        _originalEmployeeNumber = employee.EmployeeNumber;
        EmployeeNumberTextBox.Text = employee.EmployeeNumber;
        FirstNameTextBox.Text = employee.FirstName;
        LastNameTextBox.Text = employee.LastName;
        MakeCurrentCheckBox.IsChecked = isCurrent;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string employeeNumber = EmployeeNumberTextBox.Text.Trim();
        string firstName = FirstNameTextBox.Text.Trim();
        string lastName = LastNameTextBox.Text.Trim();
        if (employeeNumber.Length == 0 || firstName.Length == 0 || lastName.Length == 0)
        {
            ShowValidationError("Bitte geben Sie Mitarbeiternummer, Vorname und Nachname ein.");
            return;
        }

        Employee = new DatabaseHelper.Employee
        {
            EmployeeNumber = employeeNumber,
            FirstName = firstName,
            LastName = lastName
        };
        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
        AppLogger.Warning("validation.employee", "An employee form submission failed validation.");
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
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

        if (e.Key == Key.Enter)
        {
            SaveButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
